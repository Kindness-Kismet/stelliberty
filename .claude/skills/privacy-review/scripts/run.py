import sys

sys.dont_write_bytecode = True

import os

os.environ["PYTHONDONTWRITEBYTECODE"] = "1"

import argparse
import contextlib
from importlib.machinery import ModuleSpec
import json
from pathlib import Path
import subprocess
from types import ModuleType

SCRIPT_DIRECTORY = Path(__file__).resolve().parent
PROJECT_ROOT = SCRIPT_DIRECTORY.parents[3]

privacy_package = ModuleType("privacy_review")
privacy_package.__path__ = [str(SCRIPT_DIRECTORY)]
privacy_package.__package__ = "privacy_review"
privacy_package.__spec__ = ModuleSpec("privacy_review", loader=None, is_package=True)
sys.modules["privacy_review"] = privacy_package

from privacy_review.console import configure_output, fail, header, print_fields, print_summary, warn
from privacy_review.timer import timed_step


class ReviewFailure(Exception):
    pass


def git(repo: Path, operation: str, *args: str) -> str:
    result = subprocess.run(
        ["git", "--no-optional-locks", "-C", str(repo), *args],
        stdout=subprocess.PIPE, stderr=subprocess.PIPE,
    )
    if result.returncode:
        raise ReviewFailure(f"{operation} failed (Git exit code {result.returncode}).")
    return result.stdout.decode("utf-8", errors="replace").strip()


def create_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="run.py",
        description="Review commits on the current local branch ahead of remote main for machine-specific paths.",
        formatter_class=argparse.RawTextHelpFormatter,
        epilog=(
            "Examples:\n"
            "  run.py --check\n"
            "  run.py --check --remote upstream --json\n"
            "  run.py --check --output build/privacy-review/report.json"
        ),
    )
    parser.add_argument("--check", action="store_true", help="Refresh remote main and review the committed history.")
    parser.add_argument("--repo", type=Path, default=PROJECT_ROOT, help="Project directory; defaults to this skill's project.")
    parser.add_argument("--remote", default="origin", help="Remote containing main (default: origin).")
    parser.add_argument("--json", action="store_true", help="Return JSON on stdout; diagnostics use stderr.")
    parser.add_argument("--output", type=Path, help="Save the redacted JSON report; relative paths use the project root.")
    return parser


def review(args: argparse.Namespace) -> tuple[Path, dict]:
    from privacy_review.history import scan_history

    repo = Path(git(args.repo.resolve(), "Locate project", "rev-parse", "--show-toplevel"))
    if git(repo, "Check history depth", "rev-parse", "--is-shallow-repository") == "true":
        raise ReviewFailure("Fetch complete repository history before running this review.")
    branch_ref = git(repo, "Resolve current local branch", "symbolic-ref", "--quiet", "HEAD")
    if not branch_ref.startswith("refs/heads/"):
        raise ReviewFailure("Select a local branch before running this review.")
    branch = branch_ref.removeprefix("refs/heads/")
    remotes = git(repo, "List remotes", "remote").splitlines()
    if args.remote not in remotes:
        raise ReviewFailure("Select a configured remote containing the main branch with --remote.")

    print(header("Privacy review"), flush=True)
    print_fields([("Branch", branch), ("Base", f"{args.remote}/main")])
    base_ref = f"refs/remotes/{args.remote}/main"
    with timed_step("Refresh remote main"):
        git(
            repo, "Refresh remote main; verify access to the selected remote and its main branch",
            "fetch", "--no-tags", "--no-recurse-submodules", "--no-write-fetch-head", "--",
            args.remote, f"+refs/heads/main:{base_ref}",
        )
    head = git(repo, "Resolve current branch commit", "rev-parse", "--verify", "--end-of-options", branch_ref + "^{commit}")
    base = git(repo, "Resolve remote main commit", "rev-parse", "--verify", "--end-of-options", base_ref + "^{commit}")
    git(repo, "Find a shared history between the current branch and remote main", "merge-base", base, head)
    with timed_step("Scan committed history"):
        report = scan_history(repo, base, head)
    report.update(branch=branch, remote=args.remote, base_ref=f"{args.remote}/main", base=base, head=head)
    summary = report["summary"]
    exit_code = 2 if summary["coverage_gaps"] else int(bool(summary["candidates"]))
    report.update(ok=exit_code == 0, exit_code=exit_code)
    return repo, report


def show_result(report: dict) -> None:
    summary = report["summary"]
    status = {0: "Scan complete; path candidates are empty", 1: "Path candidates require review", 2: "Coverage requires review"}
    print(header("Review result"), flush=True)
    print_fields([
        ("Status", status[report["exit_code"]]),
        ("Base commit", report["base"]),
        ("Head commit", report["head"]),
        ("Commits", str(summary["commits"])),
        ("Files", str(summary["files"])),
        ("Blobs", str(summary["blobs"])),
        ("Candidates", str(summary["candidates"])),
        ("Coverage gaps", str(summary["coverage_gaps"])),
    ])
    for item in report["candidates"]:
        location = item["path"]
        if item.get("line") is not None:
            location += f":{item['line']}"
        if item.get("member"):
            location += f" [{item['member']}]"
        print_summary(f"Candidate {item['commit'][:12]} {location} ({item['kind']})")
    for item in report["coverage_gaps"]:
        location = item["path"]
        if item.get("member"):
            location += f" [{item['member']}]"
        print(warn(f"  warning: {item['commit'][:12]} {location} ({item['reason']})"), file=sys.stderr)


def main(argv: list[str] | None = None) -> int:
    configure_output()
    parser = create_parser()
    arguments = sys.argv[1:] if argv is None else argv
    if not arguments:
        parser.print_help()
        return 0
    args = parser.parse_args(arguments)
    if not args.check:
        parser.error("Use --check to start the review.")
    try:
        output_context = contextlib.redirect_stdout(sys.stderr) if args.json else contextlib.nullcontext()
        with output_context:
            repo, report = review(args)
        if args.output:
            destination = args.output if args.output.is_absolute() else repo / args.output
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        if args.json:
            print(json.dumps(report, ensure_ascii=False))
        else:
            show_result(report)
        return report["exit_code"]
    except (ReviewFailure, OSError, subprocess.CalledProcessError, ValueError) as error:
        if isinstance(error, ReviewFailure):
            message = str(error)
        elif isinstance(error, OSError):
            message = f"File or process operation failed ({type(error).__name__}; errno={error.errno})."
        elif isinstance(error, subprocess.CalledProcessError):
            message = f"Read Git objects failed (Git exit code {error.returncode})."
        else:
            message = f"Decode review data failed ({type(error).__name__})."
        print(fail("error: " + message), file=sys.stderr)
        if args.json:
            print(json.dumps({"ok": False, "error": message, "exit_code": 2}))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
