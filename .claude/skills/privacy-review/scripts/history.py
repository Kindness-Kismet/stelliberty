from collections import Counter
from functools import lru_cache
import subprocess

from .paths import MAX_BLOB_BYTES, safe_location, scan_bytes, scan_text, text_matches


def git(repo, *args):
    return subprocess.check_output(
        ["git", "--no-optional-locks", "-C", str(repo), *args],
        stderr=subprocess.PIPE,
    )


def changes(repo, commit, parent):
    args = ["diff-tree", "--root", "--no-commit-id", "--no-renames", "--raw", "--no-abbrev", "-r", "-z"]
    if parent:
        args.append(parent)
    args.append(commit)
    parts = git(repo, *args).split(b"\0")
    for index in range(0, len(parts) - 1, 2):
        old_mode, new_mode, old, new, status = parts[index].decode("ascii").split()
        yield {
            "path": parts[index + 1].decode("utf-8", "replace"),
            "old_mode": old_mode.lstrip(":"), "new_mode": new_mode,
            "old": old, "new": new, "status": status,
        }


def identity(hit):
    return hit["kind"], hit.get("member", ""), hit["fingerprint"]


def public_hit(hit):
    return {key: value for key, value in hit.items() if key != "fingerprint"}


class BlobReader:
    def __init__(self, repo):
        self.blobs = set()
        self.process = subprocess.Popen(
            ["git", "--no-optional-locks", "-C", str(repo), "cat-file", "--batch-command"],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
        )

    def __enter__(self):
        return self

    def __exit__(self, exception_type, exception, traceback):
        self.process.stdin.close()
        self.process.stdout.close()
        self.process.wait()
        self.scan.cache_clear()

    def command(self, action, object_id):
        self.process.stdin.write(f"{action} {object_id}\n".encode("ascii"))
        self.process.stdin.flush()
        header = self.process.stdout.readline().split()
        if len(header) != 3 or header[1] != b"blob":
            raise OSError("Expected a complete Git blob header.")
        return int(header[2])

    @lru_cache(maxsize=128)
    def scan(self, object_id):
        size = self.command("info", object_id)
        if size > MAX_BLOB_BYTES:
            return {"hits": [], "gaps": [{"reason": "blob_size_limit"}]}
        size = self.command("contents", object_id)
        raw = self.process.stdout.read(size)
        if len(raw) != size or self.process.stdout.read(1) != b"\n":
            raise OSError("Expected complete Git blob contents.")
        self.blobs.add(object_id)
        return scan_bytes(raw)


def scan_history(repo, base, head):
    commits = git(repo, "rev-list", "--reverse", "--topo-order", head, "--not", base).decode("ascii").splitlines()
    report = {"base": base, "head": head, "commits": commits, "candidates": [], "coverage_gaps": []}
    files = set()
    seen_candidates, seen_gaps = set(), set()
    locations = {}

    def location(path):
        if path not in locations:
            locations[path] = safe_location(path, f"<redacted-file-{len(locations) + 1}>")
        return locations[path]

    def add_hits(commit, path, hits, previous=()):
        prior = Counter(identity(hit) for hit in previous)
        for hit in hits:
            value = identity(hit)
            if prior[value]:
                prior[value] -= 1
                continue
            key = (commit, path, hit.get("line"), value)
            if key in seen_candidates:
                continue
            seen_candidates.add(key)
            report["candidates"].append({"commit": commit, "path": path, **public_hit(hit)})

    def add_gaps(commit, path, gaps):
        for gap in gaps:
            key = commit, path, gap.get("member", ""), gap["reason"]
            if key in seen_gaps:
                continue
            seen_gaps.add(key)
            report["coverage_gaps"].append({"commit": commit, "path": path, **gap})

    with BlobReader(repo) as reader:
        for commit in commits:
            parent_text = git(repo, "show", "-s", "--format=%P", commit).decode("ascii").split()
            message = git(repo, "show", "-s", "--format=%B", commit)
            hits, binary = scan_text(message)
            add_hits(commit, "<commit-message>", hits)
            if binary:
                add_gaps(commit, "<commit-message>", [{"reason": "commit_message_encoding_requires_review"}])
            for change in changes(repo, commit, parent_text[0] if parent_text else None):
                path = change["path"]
                files.add(path)
                shown_path = location(path)
                if change["status"] == "A":
                    add_hits(commit, shown_path, text_matches(path))
                if "160000" in (change["old_mode"], change["new_mode"]):
                    add_gaps(commit, shown_path, [{
                        "reason": "submodule_history_requires_review",
                        "old": change["old"], "new": change["new"],
                    }])
                if change["new_mode"] in ("000000", "160000"):
                    continue
                current = reader.scan(change["new"])
                previous = {"hits": [], "gaps": []}
                if change["old_mode"] not in ("000000", "160000"):
                    previous = reader.scan(change["old"])
                add_hits(commit, shown_path, current["hits"], previous["hits"])
                add_gaps(commit, shown_path, current["gaps"])
                if current["hits"] and previous["gaps"]:
                    add_gaps(commit, shown_path, [{"reason": "comparison_baseline_requires_review"}])
        report["summary"] = {
            "commits": len(commits), "files": len(files), "blobs": len(reader.blobs),
            "candidates": len(report["candidates"]), "coverage_gaps": len(report["coverage_gaps"]),
        }
    return report
