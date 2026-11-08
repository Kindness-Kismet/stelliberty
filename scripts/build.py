#!/usr/bin/env python3
import os
import sys

sys.dont_write_bytecode = True
os.environ["PYTHONDONTWRITEBYTECODE"] = "1"
# 进度输出含 · 与 ✅，cp1252/cp936 控制台会抛 UnicodeEncodeError，故自行接管标准流编码。
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.stderr.reconfigure(encoding="utf-8", errors="replace")

import argparse
import subprocess

from build_support.builder import build
from build_support.console import header
from build_support.help import MultilineHelpFormatter, format_choice_help
from build_support.metadata import read_app_metadata, resolve_metadata_version
from build_support.models import BuildRequest
from build_support.platforms import resolve_platforms, split_rid

BUILD_PLATFORM_HELP = format_choice_help(
    "Target platform.",
    [
        ("current", "Current host platform"),
        ("desktop", "Windows x64, Linux x64, and macOS ARM64"),
        ("win-x64", "Windows x64"),
        ("win-arm64", "Windows ARM64"),
        ("linux-x64", "Linux x64"),
        ("linux-arm64", "Linux ARM64"),
        ("macos-x64", "macOS x64"),
        ("macos-arm64", "macOS ARM64"),
    ],
)
PACK_HELP = format_choice_help(
    "Package format.",
    [
        ("app", "Create a retained macOS app bundle"),
        ("zip", "Create a zip archive"),
        ("installer", "Create a platform installer"),
        ("all", "Create every package format"),
    ],
)


def main() -> None:
    args = parse_args()
    request = create_build_request(args)
    print_environment(request)
    build(request)


def print_environment(request: BuildRequest) -> None:
    if request.platforms:
        rid, _ = request.platforms[0]
        host_platform, host_arch = split_rid(rid)
    else:
        host_platform, host_arch = "Unknown", "Unknown"

    configurations = ", ".join(c.capitalize() for c in request.configurations)
    rust_version = get_tool_version(["rustc", "--version"])
    dotnet_version = get_tool_version(["dotnet", "--version"])

    print(header("Current build environment"))
    print(f"  Platform       {host_platform}")
    print(f"  Architecture   {host_arch}")
    print(f"  Configuration  {configurations}")
    print(f"  Version        {request.metadata.version}")
    print(f"  Rust   {rust_version}")
    print(f"  .NET   {dotnet_version}")
    print()


def get_tool_version(command: list[str]) -> str:
    try:
        result = subprocess.run(command, capture_output=True, text=True, errors="replace", timeout=5)
        return result.stdout.strip() or "Unknown"
    except Exception:
        return "Unknown"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Build the Stelliberty desktop app",
        formatter_class=MultilineHelpFormatter,
    )
    configuration = parser.add_mutually_exclusive_group()
    configuration.add_argument("--dev", action="store_true", help="Build the Debug configuration")
    configuration.add_argument("--all", action="store_true", help="Build both Debug and Release")
    parser.add_argument(
        "--platform",
        metavar="TARGET",
        default="current",
        help=BUILD_PLATFORM_HELP,
    )
    parser.add_argument(
        "--pack",
        choices=["app", "zip", "installer", "all"],
        metavar="FORMAT",
        default=None,
        help=PACK_HELP,
    )
    parser.add_argument(
        "--version",
        metavar="VERSION",
        default=None,
        help="Override app version for this build only (does not rewrite Directory.Build.props)",
    )
    parser.add_argument("--clean", action="store_true", help="Clean the target output directory before building")
    return parser.parse_args()


def create_build_request(args: argparse.Namespace) -> BuildRequest:
    return BuildRequest(
        configurations=resolve_configurations(args),
        platforms=resolve_platforms(args.platform),
        metadata=resolve_metadata_version(read_app_metadata(), args.version),
        pack_format=args.pack,
        clean=args.clean,
    )


def resolve_configurations(args: argparse.Namespace) -> list[str]:
    if args.all:
        return ["dev", "release"]

    if args.dev:
        return ["dev"]

    return ["release"]


if __name__ == "__main__":
    main()
