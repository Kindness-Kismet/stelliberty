import sys

sys.dont_write_bytecode = True

import os

os.environ["PYTHONDONTWRITEBYTECODE"] = "1"

import time
from contextlib import contextmanager

from .console import header, timing


@contextmanager
def timed_step(label: str):
    print(header(label), flush=True)
    started_at = time.perf_counter()
    try:
        yield
    finally:
        print(timing(time.perf_counter() - started_at), flush=True)
        print()
