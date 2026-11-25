"""Manage the exact Debug output, connection and local command serialization."""
import sys
sys.dont_write_bytecode = True
import os
os.environ["PYTHONDONTWRITEBYTECODE"] = "1"

import json
import platform
import re
import socket
import subprocess
import time
from contextlib import contextmanager
from pathlib import Path
from xml.etree import ElementTree

ROOT = Path(os.environ.get("STELLIBERTY_DEBUG_ROOT", Path(__file__).resolve().parents[4])).resolve()
BUILD_PROPS = ROOT / "Directory.Build.props"
PORT = 20000
COMMAND_TIMEOUT_SECONDS = 180
SCREENSHOT_DIR = ROOT / "build/screenshots"
COMMAND_LOCK_PATH = ROOT / "build/app-debug-cli.lock"
AUTO_HOT_RELOAD_WAIT_SECONDS = 1.5
IGNORED_SOURCE_DIRS = {"bin", "obj", "target", "__pycache__"}

sys.path.insert(0, str(ROOT / "scripts"))
from build_support.processes import listening_process_ids, process_binary_path, running_process_ids, stop_running_paths

def build_control_command_catalog(root: Path) -> str:
    views_dir = root / "src" / "Stelliberty.Desktop" / "Views"
    lines: list[str] = []
    for path in sorted(views_dir.rglob("*.axaml")):
        entries = control_entries(path)
        if not entries:
            continue
        lines.append(f"# {path.stem.removesuffix('View')}")
        lines.extend(entries)
        lines.append("")

    return "\n".join(lines).rstrip()

def control_entries(path: Path) -> list[str]:
    text = path.read_text(encoding="utf-8")
    entries: list[str] = []
    for match in re.finditer(r"<(?P<tag>[\w:.]+)[^>]*AutomationProperties\.AutomationId=\"(?P<id>[^\"]+)\"", text):
        tag = match.group("tag").split(":")[-1]
        automation_id = match.group("id")
        entries.extend(command_lines_for_control(tag, automation_id))
        entries.extend(bound_automation_command_lines(tag, automation_id))
    return entries

def bound_automation_command_lines(tag: str, automation_id: str) -> list[str]:
    if automation_id == "{Binding DetailAutomationId}":
        return command_lines_for_control(tag, "Connections.Row.<ConnectionId>.DetailButton")
    if automation_id == "{Binding CloseAutomationId}":
        return command_lines_for_control(tag, "Connections.Row.<ConnectionId>.CloseButton")
    return []

def command_lines_for_control(tag: str, automation_id: str) -> list[str]:
    if not automation_id or "{" in automation_id:
        return []
    if tag == "TextBox":
        return [f"control.input {automation_id} <text>"]
    if tag == "ComboBox":
        lines = [f"dropdown.open {automation_id}", f"dropdown.select {automation_id} <index>"]
        if automation_id.endswith(".MenuButton"):
            lines.insert(0, f"# Menu item: dropdown.select {automation_id} <index>")
        return lines
    if tag in {"Button", "ToggleButton", "ToggleSwitch", "RadioButton", "CheckBox"}:
        return [f"control.click {automation_id}"]
    return [f"# Assert control exists: {automation_id}"]

def debug_dir() -> Path:
    app_name, version = read_app_metadata()
    return ROOT / "build" / f"{app_name}-{display_version(version)}-{current_platform()}-dev"

def app_exe() -> Path:
    app_name, _ = read_app_metadata()
    suffix = ".exe" if sys.platform == "win32" else ""
    return debug_dir() / f"{app_name}{suffix}"

def is_debug_app_exe(path: Path) -> bool:
    app_name, _ = read_app_metadata()
    suffix = ".exe" if sys.platform == "win32" else ""
    resolved = path.resolve()
    directory = resolved.parent
    if resolved.name != f"{app_name}{suffix}":
        return False
    expected_prefix = f"{app_name}-v"
    expected_suffix = f"-{current_platform()}-dev"
    return directory.parent.resolve() == (ROOT / "build").resolve() \
        and directory.name.startswith(expected_prefix) \
        and directory.name.endswith(expected_suffix) \
        and len(directory.name) > len(expected_prefix) + len(expected_suffix)

def app_log_path() -> Path:
    return debug_dir() / ".app-debug-app.log"

def launch_app(startup_delay_ms: int = 0, silent_start: bool = False, no_focus: bool = True) -> None:
    log = open(app_log_path(), "a", encoding="utf-8")
    kwargs: dict = {"stdin": subprocess.DEVNULL, "stdout": log, "stderr": subprocess.STDOUT}
    kwargs["env"] = {**os.environ, "STELLIBERTY_DEBUG_STARTUP_DELAY_MS": str(startup_delay_ms)}
    if sys.platform == "win32":
        kwargs["creationflags"] = subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP
    else:
        kwargs["start_new_session"] = True
    try:
        arguments = [str(app_exe()), "--silent-start" if silent_start else "--show-ui"]
        if no_focus:
            arguments.append("--no-focus")
        subprocess.Popen(arguments, cwd=debug_dir(), **kwargs)
    finally:
        log.close()

def app_log_excerpt() -> str:
    path = app_log_path()
    if not path.exists():
        return ""

    text = path.read_text(encoding="utf-8", errors="replace").strip()
    if not text:
        return ""

    tail = "\n".join(text.splitlines()[-20:])
    return f"\n--- Last 20 app output lines ---\n{tail}"

def current_platform() -> str:
    machine = platform.machine().lower()
    arch = "arm64" if machine in ("arm64", "aarch64") else "x64"

    if sys.platform == "win32":
        return f"win-{arch}"

    if sys.platform == "darwin":
        return f"macos-{arch}"

    return f"linux-{arch}"

def read_app_metadata() -> tuple[str, str]:
    # 仅解析 Directory.Build.props 的字面属性，不执行 MSBuild 求值。
    root = ElementTree.parse(BUILD_PROPS).getroot()
    return require_property(root, "AppName"), require_property(root, "AppVersion")

def display_version(version: str) -> str:
    return version if version.lower().startswith("v") else f"v{version}"

def require_property(root: ElementTree.Element, name: str) -> str:
    value = root.findtext(f".//{name}")
    if value is None or value.strip() == "":
        raise RuntimeError(f"Directory.Build.props is missing {name}")
    return value.strip()

def require_running_debug_app() -> None:
    pid = port_owner_pid()
    if pid is None:
        raise RuntimeError("No Debug instance is running; focus-mode changes cannot start an app")
    if not is_expected_process(pid):
        raise RuntimeError(f"Port {PORT} is occupied by unexpected PID {pid}.")

def ensure_debug_app(no_focus: bool = True) -> None:
    pid = port_owner_pid()
    if pid is not None:
        if not is_expected_process(pid):
            raise RuntimeError(f"Port {PORT} is occupied by unexpected PID {pid}.")
        if has_newer_xaml(app_exe()):
            wait_for_auto_hot_reload()
        return

    ensure_debug_package_exists()
    print("[app-debug] Starting app; waiting for debug port...", flush=True)
    launch_app(no_focus=no_focus)
    wait_for_port()
    print("[app-debug] App is ready.", flush=True)

def wait_for_auto_hot_reload() -> None:
    time.sleep(AUTO_HOT_RELOAD_WAIT_SECONDS)

def restart_debug_app(startup_delay_ms: int = 0, silent_start: bool = False, no_focus: bool = True) -> None:
    if not 0 <= startup_delay_ms <= 30000:
        raise ValueError("startup-delay-ms must be between 0 and 30000")
    stop_debug_app_if_running()

    ensure_debug_package_exists()
    print("[app-debug] Starting app; waiting for debug port...", flush=True)
    launch_app(startup_delay_ms=startup_delay_ms, silent_start=silent_start, no_focus=no_focus)
    wait_for_port()
    print("[app-debug] App is ready.", flush=True)

def ensure_debug_package_exists() -> None:
    if not app_exe().exists():
        raise RuntimeError("Debug output does not exist; run python scripts/build.py --dev first.")

def should_ignore_source_path(path: Path) -> bool:
    try:
        return any(part in IGNORED_SOURCE_DIRS for part in path.relative_to(ROOT).parts)
    except ValueError:
        return False

def has_newer_xaml(target: Path) -> bool:
    if not target.exists():
        return False

    target_time = target.stat().st_mtime
    for path in (ROOT / "src").rglob("*.axaml"):
        if should_ignore_source_path(path):
            continue
        if path.is_file() and path.stat().st_mtime > target_time:
            return True

    return False

def stop_debug_app_if_running(output_dir: Path | None = None) -> None:
    expected_paths = output_binary_paths(output_dir)
    pid = port_owner_pid()
    if output_dir is None and pid is not None:
        actual = process_binary_path(pid)
        if actual is None or not is_debug_app_exe(actual):
            raise RuntimeError(f"Port {PORT} is occupied by unexpected PID {pid}; refusing to stop.")
        expected_paths.add(actual.resolve())
    stop_running_paths(expected_paths, PORT)

def output_binary_paths(output_dir: Path | None = None) -> set[Path]:
    if output_dir is None:
        return {app_exe().resolve()}

    directory = output_dir.resolve()
    app_name, _ = read_app_metadata()
    pattern = rf"{re.escape(app_name)}-v.+-{re.escape(current_platform())}(?:-dev)?"
    if directory.parent != (ROOT / "build").resolve() or re.fullmatch(pattern, directory.name) is None:
        raise ValueError(f"Not a build output directory for this repository: {directory}")

    suffix = ".exe" if sys.platform == "win32" else ""
    return {
        directory / f"{app_name}{suffix}",
        directory / "data" / "deps" / f"{app_name}_ui{suffix}",
        directory / f"{app_name}_ui{suffix}",
    }

def debug_app_pids(expected_paths: set[Path] | None = None) -> list[int]:
    paths = output_binary_paths() if expected_paths is None else expected_paths
    return [pid for path in paths for pid in running_process_ids(path)]


def port_owner_pid() -> int | None:
    pids = listening_process_ids(PORT)
    if not pids:
        return None
    return next((pid for pid in pids if is_expected_process(pid)), pids[0])


def is_expected_process(pid: int) -> bool:
    actual = process_binary_path(pid)
    return actual is not None and is_debug_app_exe(actual)

def wait_for_port() -> None:
    deadline = time.time() + 30
    while time.time() < deadline:
        pid = port_owner_pid()
        if pid is not None and is_expected_process(pid):
            return
        time.sleep(0.2)

    raise TimeoutError("Debug control port was not ready within 30s." + app_log_excerpt())

@contextmanager
def serialized_debug_command():
    COMMAND_LOCK_PATH.parent.mkdir(parents=True, exist_ok=True)
    with COMMAND_LOCK_PATH.open("a+b") as lock_file:
        if sys.platform == "win32":
            import msvcrt

            deadline = time.time() + COMMAND_TIMEOUT_SECONDS + 30
            while True:
                try:
                    lock_file.seek(0)
                    msvcrt.locking(lock_file.fileno(), msvcrt.LK_NBLCK, 1)
                    break
                except OSError as exception:
                    if time.time() >= deadline:
                        raise TimeoutError("Timed out waiting for the debug CLI serial lock.") from exception
                    time.sleep(0.1)

            try:
                yield
            finally:
                lock_file.seek(0)
                msvcrt.locking(lock_file.fileno(), msvcrt.LK_UNLCK, 1)
            return

        import fcntl

        fcntl.flock(lock_file.fileno(), fcntl.LOCK_EX)
        try:
            yield
        finally:
            fcntl.flock(lock_file.fileno(), fcntl.LOCK_UN)

def send_command(line: str, *, allow_disconnect: bool = False) -> str:
    with serialized_debug_command():
        with socket.create_connection(("127.0.0.1", PORT), timeout=10) as client:
            client.settimeout(COMMAND_TIMEOUT_SECONDS)
            client.sendall((line + "\n").encode("utf-8"))
            try:
                with client.makefile("r", encoding="utf-8") as reader:
                    response = reader.read().rstrip("\n")
            except ConnectionResetError:
                if not allow_disconnect:
                    raise
                response = ""

    if response == "OK" or allow_disconnect and not response:
        return ""

    if response.startswith("OK "):
        return response[3:]

    raise RuntimeError(f"Command failed: {line} -> {response}")

def latest_screenshot() -> Path | None:
    if not SCREENSHOT_DIR.exists():
        return None

    screenshots = sorted(SCREENSHOT_DIR.glob("*.png"), key=lambda path: path.stat().st_mtime)
    return screenshots[-1] if screenshots else None

def wait_for_new_screenshot(previous: Path | None) -> Path:
    deadline = time.time() + 5
    while time.time() < deadline:
        current = latest_screenshot()
        if current is not None and current != previous:
            return current
        time.sleep(0.1)

    raise TimeoutError("Screenshot was not created.")
