"""Dispatch registered commands to existing app and verification capabilities."""
import argparse
import importlib
import importlib.util
import json
import sys

from . import runtime
from .commands import build_server_command


def read_result(response: str):
    if not response:
        return None
    try:
        return json.loads(response)
    except json.JSONDecodeError:
        return response


def with_focus_mode(command: str, mode: str | None) -> str:
    return command if mode is None else f"focus.mode {mode} {command}"


def execute_command(args: argparse.Namespace):
    match args.cmd:
        case "window.verify-hover":
            module = importlib.import_module("app_debug.verify_window_hover")
            module.run(runtime, assert_cleared=args.assert_cleared)
            return {"verified": True}
        case "app.verify":
            runtime.stop_debug_app_if_running()
            spec = importlib.util.spec_from_file_location("post_build_tests", runtime.ROOT / ".github/scripts/post_build_tests.py")
            module = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(module)
            os_family = "windows" if sys.platform == "win32" else "macos" if sys.platform == "darwin" else "linux"
            runner = module.PostBuildTests(runtime.app_exe(), runtime.debug_dir(), os_family, shortcut_only=args.shortcuts)
            try:
                runner.run()
            finally:
                runner.cleanup()
            return {"verified": True}
        case "app.start" | "app.ensure":
            if args.focus_mode is not None:
                runtime.require_running_debug_app()
            else:
                runtime.ensure_debug_app(no_focus=args.launch_no_focus)
            command = "window.show" if args.cmd == "app.ensure" else "app.status"
            return read_result(runtime.send_command(with_focus_mode(command, args.focus_mode)))
        case "app.status":
            runtime.require_running_debug_app()
            return read_result(runtime.send_command("app.status"))
        case "app.restart":
            runtime.restart_debug_app(startup_delay_ms=args.startup_delay_ms, silent_start=args.silent_start, no_focus=args.launch_no_focus)
            return read_result(runtime.send_command("app.status"))
        case "app.stop":
            runtime.stop_debug_app_if_running(args.output_dir)
            return {"stopped": True}
        case "control.catalog":
            return runtime.build_control_command_catalog(runtime.ROOT)
        case "screenshot.take":
            runtime.ensure_debug_app()
            before = runtime.latest_screenshot()
            runtime.send_command("screenshot.take")
            return {"path": str(runtime.wait_for_new_screenshot(before)), "format": "png"}
        case _:
            command = build_server_command(args)
            if args.focus_mode is not None:
                runtime.require_running_debug_app()
            else:
                runtime.ensure_debug_app()
            return read_result(runtime.send_command(with_focus_mode(command, args.focus_mode)))
