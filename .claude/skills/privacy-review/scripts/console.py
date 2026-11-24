import sys

sys.dont_write_bytecode = True

import os

os.environ["PYTHONDONTWRITEBYTECODE"] = "1"

from typing import TextIO

CYAN = "\033[36m"
DIM = "\033[2m"
GREEN = "\033[32m"
YELLOW = "\033[33m"
RED = "\033[31m"
BOLD = "\033[1m"
RESET = "\033[0m"


def configure_output() -> None:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")


def paint(text: str, *codes: str, stream: TextIO | None = None) -> str:
    target = sys.stdout if stream is None else stream
    if target.isatty() and not os.environ.get("NO_COLOR"):
        return "".join(codes) + text + RESET
    return text


def header(text: str) -> str:
    return paint(f"==> {text}", CYAN, BOLD)


def timing(elapsed: float) -> str:
    return paint(f"  · {elapsed:.2f}s", GREEN)


def print_fields(fields: list[tuple[str, str]]) -> None:
    for label, value in fields:
        print(f"  {label:<13}  {value}", flush=True)
    print()


def print_summary(*lines: str) -> None:
    for line in lines:
        print(f"  {line}", flush=True)


def warn(text: str, *, stream: TextIO | None = None) -> str:
    target = sys.stderr if stream is None else stream
    return paint(text, YELLOW, stream=target)


def fail(text: str, *, stream: TextIO | None = None) -> str:
    target = sys.stderr if stream is None else stream
    return paint(text, RED, BOLD, stream=target)


def format_bytes(num_bytes: int) -> str:
    units = ("B", "KB", "MB", "GB")
    value = float(num_bytes)
    for unit in units:
        if value < 1024 or unit == "GB":
            return f"{int(value)} B" if unit == "B" else f"{value:.1f} {unit}"
        value /= 1024
