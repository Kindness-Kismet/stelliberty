"""Synchronize the project's skill source and mirror without loading skill code."""
import sys

sys.dont_write_bytecode = True

import os

os.environ["PYTHONDONTWRITEBYTECODE"] = "1"

import argparse
import ctypes
import shutil
import subprocess
import time
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE_DIRECTORY = Path(".codex/skills")
MIRROR_DIRECTORY = Path(".claude/skills")
SOURCE_SUFFIXES = {".md", ".py"}


def source_files(directory: Path) -> dict[str, Path]:
    if not directory.is_dir():
        raise RuntimeError(f"Skill directory is missing: {directory}")
    files = {}
    for path in sorted(directory.rglob("*")):
        relative = path.relative_to(directory)
        if "__pycache__" in relative.parts or ".bak" in path.name:
            continue
        if path.is_symlink():
            raise RuntimeError(f"Skill symlinks are not supported: {relative.as_posix()}")
        if path.is_file() and path.suffix in SOURCE_SUFFIXES:
            files[relative.as_posix()] = path
    return files


def differences(expected: dict[str, Path], actual: dict[str, Path]) -> list[str]:
    return [
        name for name in sorted(expected.keys() | actual.keys())
        if name not in expected or name not in actual
        or expected[name].read_bytes() != actual[name].read_bytes()
    ]


def recycle_file(path: Path, mirror: Path) -> None:
    if not path.resolve().is_relative_to(mirror.resolve()) or not path.is_file():
        raise RuntimeError(f"Refusing to recycle a file outside the skill mirror: {path}")
    if sys.platform == "win32":
        from ctypes import wintypes

        class FileOperation(ctypes.Structure):
            _fields_ = [("hwnd", wintypes.HWND), ("operation", wintypes.UINT),
                        ("source", wintypes.LPCWSTR), ("target", wintypes.LPCWSTR),
                        ("flags", ctypes.c_ushort), ("aborted", wintypes.BOOL),
                        ("mapping", ctypes.c_void_p), ("title", wintypes.LPCWSTR)]

        # Windows Shell 回收精确文件，保留撤销能力并禁止路径通配符。
        operation = FileOperation(None, 3, str(path.resolve()) + "\0", None, 0x40 | 0x10 | 0x04 | 0x400, False, None, None)
        result = ctypes.windll.shell32.SHFileOperationW(ctypes.byref(operation))
        if result or operation.aborted:
            raise RuntimeError(f"Recycling mirror file failed (Shell code {result}): {path}")
        return
    if sys.platform == "darwin":
        trash = Path.home() / ".Trash"
        trash.mkdir(exist_ok=True)
        shutil.move(str(path), str(trash / f"{path.name}.{uuid.uuid4().hex}"))
        return
    command = shutil.which("trash") or shutil.which("trash-put")
    if command is None:
        raise RuntimeError("Install trash-cli to recycle removed mirror files.")
    subprocess.run([command, str(path)], check=True)


def synchronize(root: Path, check: bool) -> tuple[int, int]:
    source = root / SOURCE_DIRECTORY
    mirror = root / MIRROR_DIRECTORY
    if source.is_symlink() or mirror.is_symlink():
        raise RuntimeError("Skill source and mirror must be real directories.")
    source_paths = source_files(source)
    if not source_paths:
        raise RuntimeError("Skill source contains no maintained files.")
    mirror_paths = source_files(mirror) if mirror.exists() else {}
    changes = differences(source_paths, mirror_paths)
    if check:
        if changes:
            raise RuntimeError("Skills require synchronization: " + ", ".join(changes))
        return len(source_paths), 0

    mirror.mkdir(parents=True, exist_ok=True)
    for name in changes:
        destination = mirror / name
        if name not in source_paths:
            recycle_file(destination, mirror)
            continue
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(source_paths[name].read_bytes())
    if differences(source_files(source), source_files(mirror)):
        raise RuntimeError("Skill synchronization verification failed.")
    return len(source_paths), len(changes)


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    parser = argparse.ArgumentParser(description="Synchronize .codex/skills to .claude/skills and compare their file sets and bytes.", formatter_class=argparse.RawTextHelpFormatter)
    operation = parser.add_mutually_exclusive_group(required=True)
    operation.add_argument("--sync", action="store_true", help="Copy source changes and recycle obsolete mirror files.")
    operation.add_argument("--check", action="store_true", help="Check file sets and bytes without writing.")
    parser.add_argument("--root", type=Path, default=ROOT, help="Project root; defaults to the project containing this script.")
    if len(sys.argv) == 1:
        parser.print_help()
        return 0
    args = parser.parse_args()
    started_at = time.perf_counter()
    print("==> Check skill mirrors" if args.check else "==> Synchronize skills", flush=True)
    try:
        count, changes = synchronize(args.root.resolve(), args.check)
    except (RuntimeError, ValueError) as error:
        print("  error: " + str(error), file=sys.stderr)
        return 1
    except OSError as error:
        print(f"  error: Skill file operation failed ({type(error).__name__}; errno={error.errno}; filename={error.filename}).", file=sys.stderr)
        return 1
    print(f"  Files          {count}\n  Updated        {changes}\n  · {time.perf_counter() - started_at:.2f}s\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
