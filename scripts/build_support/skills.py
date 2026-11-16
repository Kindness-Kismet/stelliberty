"""Synchronize local assistant skills without build or runtime dependencies."""

from __future__ import annotations

import sys

sys.dont_write_bytecode = True

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import shutil

os.environ["PYTHONDONTWRITEBYTECODE"] = "1"

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / ".codex" / "skills"
MIRROR = ROOT / ".claude" / "skills"
STATE = ROOT / "build" / "skill-sync" / "baseline.json"


def inventory(directory: Path) -> dict[str, bytes]:
    result = {}
    for path in sorted(directory.rglob("*")):
        if not path.is_file() or "__pycache__" in path.parts:
            continue
        if path.suffix in {".pyc", ".pyo", ".bak", ".tmp"} or ".bak." in path.name:
            continue
        if not path.resolve().is_relative_to(directory.resolve()):
            raise ValueError(f"Skill file escapes its directory: {path.relative_to(directory)}")
        result[path.relative_to(directory).as_posix()] = path.read_bytes()
    return result


def fingerprint(content: bytes) -> str:
    return hashlib.sha256(content).hexdigest()


def synchronize(check: bool) -> int:
    if not SOURCE.is_dir():
        print("Error: Missing .codex/skills source directory.", file=sys.stderr)
        return 1
    if not MIRROR.resolve().is_relative_to(ROOT):
        raise ValueError("Skill mirror escapes the project directory.")

    source = inventory(SOURCE)
    mirror = inventory(MIRROR)
    changed = sorted(key for key in source.keys() | mirror.keys() if source.get(key) != mirror.get(key))
    if check:
        for key in changed:
            print(f"  Mismatch: {key}")
        print(f"Skills {'differ' if changed else 'match'}: {len(source)} source files.")
        return int(bool(changed))

    baseline = json.loads(STATE.read_text(encoding="utf-8")) if STATE.exists() else None
    drift = []
    if baseline is None:
        for key, content in mirror.items():
            # 首次同步只接受旧镜像命令示例的目录差异。
            normalized = content.replace(b"python .claude/skills/", b"python .codex/skills/")
            if content != source.get(key) and normalized != source.get(key):
                drift.append(key)
    else:
        for key in mirror.keys() | baseline.keys():
            current = mirror.get(key)
            digest = fingerprint(current) if current is not None else None
            if digest != baseline.get(key) and current != source.get(key):
                drift.append(key)
    if drift:
        print("Error: Claude mirror has independent changes; reconcile them with Codex first.", file=sys.stderr)
        for key in sorted(drift):
            print(f"  Drift: {key}", file=sys.stderr)
        return 1

    backup = STATE.parent / "backups" / datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    for key in changed:
        target = MIRROR / key
        if not target.resolve().is_relative_to(MIRROR.resolve()):
            raise ValueError(f"Skill target escapes the mirror: {key}")
        if key in mirror:
            archived = backup / key
            archived.parent.mkdir(parents=True, exist_ok=True)
            if key not in source:
                shutil.move(str(target), str(archived))
                continue
            shutil.copy2(target, archived)
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(source[key])

    STATE.parent.mkdir(parents=True, exist_ok=True)
    STATE.write_text(json.dumps({key: fingerprint(value) for key, value in source.items()}, indent=2) + "\n", encoding="utf-8")
    print(f"Skills synchronized: {len(source)} files, {len(changed)} changes.")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    modes = parser.add_mutually_exclusive_group(required=True)
    modes.add_argument("--sync", action="store_true", help="Sync Codex skills to Claude and save a baseline.")
    modes.add_argument("--check", action="store_true", help="Check file sets and bytes without writing.")
    if len(sys.argv) == 1:
        parser.print_help()
        return 0
    args = parser.parse_args()
    try:
        return synchronize(args.check)
    except (OSError, ValueError) as error:
        if isinstance(error, OSError):
            print(f"Error: Skill synchronization failed ({type(error).__name__}, errno={error.errno}).", file=sys.stderr)
        else:
            print(f"Error: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
