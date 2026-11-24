"""Check the line limit of maintained application, UI and automation sources."""
import sys

sys.dont_write_bytecode = True

import os

os.environ["PYTHONDONTWRITEBYTECODE"] = "1"

import argparse
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MAX_CODE_LINES = 2400
CODE_DIRECTORIES = ("src", "native", "tests", "scripts", ".github/scripts", ".codex/skills", ".claude/skills")
CODE_SUFFIXES = {".cs", ".rs", ".py", ".axaml", ".sh", ".ps1"}
GENERATED_DIRECTORIES = {"bin", "obj", "target", "Generated", "__pycache__"}


def maintained_code_files(root: Path):
    for name in CODE_DIRECTORIES:
        directory = root / name
        if not directory.is_dir():
            continue
        for path in sorted(directory.rglob("*")):
            relative = path.relative_to(root)
            if any(part in GENERATED_DIRECTORIES for part in relative.parts):
                continue
            if path.is_file() and path.suffix in CODE_SUFFIXES:
                yield path


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    parser = argparse.ArgumentParser(description="Check maintained code files against the 2400-line limit, including comments and blank lines.")
    parser.add_argument("--check", action="store_true", help="Run the read-only line-limit check.")
    parser.add_argument("--root", type=Path, default=ROOT, help="Project root; defaults to this script's project.")
    if len(sys.argv) == 1:
        parser.print_help()
        return 0
    args = parser.parse_args()
    if not args.check:
        parser.error("Use --check to run the check.")
    root = args.root.resolve()
    if not root.is_dir():
        parser.error("Project root does not exist.")
    started_at = time.perf_counter()
    print("==> Check code file limits", flush=True)
    count, failures = 0, 0
    try:
        for path in maintained_code_files(root):
            count += 1
            line_count = len(path.read_bytes().splitlines())
            if line_count > MAX_CODE_LINES:
                failures += 1
                print(f"  error: {path.relative_to(root).as_posix()} has {line_count} lines (limit {MAX_CODE_LINES}).", file=sys.stderr)
    except OSError as error:
        print(f"  error: Reading code failed ({type(error).__name__}; errno={error.errno}; filename={error.filename}).", file=sys.stderr)
        return 1
    if not count:
        print("  error: Project contains no maintained code files.", file=sys.stderr)
        return 1
    print(f"  Files          {count}\n  Failed         {failures}\n  · {time.perf_counter() - started_at:.2f}s\n")
    return int(bool(failures))


if __name__ == "__main__":
    raise SystemExit(main())
