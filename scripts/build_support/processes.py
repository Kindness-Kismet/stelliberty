import json
import os
import shlex
import subprocess
import sys
import time
from pathlib import Path

from build_support.models import AppMetadata
from build_support.paths import BUILD_DIR, DEPS_DIRECTORY


def close_running_output_app(metadata: AppMetadata, output_dir: Path) -> None:
    close_running_binary(output_binary_path(metadata.app_name, output_dir))
    ui_name = f"{metadata.app_name}_ui"
    close_running_binary(output_binary_path(ui_name, output_dir / DEPS_DIRECTORY))
    close_running_binary(output_binary_path(ui_name, output_dir))


def close_running_binary(binary_path: Path) -> None:
    if not binary_path.exists():
        return

    resolved_binary = binary_path.resolve()
    ensure_build_output_path(resolved_binary)

    process_ids = running_process_ids(resolved_binary)
    if not process_ids:
        return

    for process_id in process_ids:
        print(f"  Stopping blocking process PID {process_id}: {resolved_binary}", flush=True)
        terminate_process(process_id, force=False)

    if wait_for_processes_exit(process_ids, timeout_seconds=5):
        return

    remaining = [process_id for process_id in process_ids if process_exists(process_id)]
    for process_id in remaining:
        print(f"  Force-stopping blocking process PID {process_id}: {resolved_binary}", flush=True)
        terminate_process(process_id, force=True)

    if not wait_for_processes_exit(remaining, timeout_seconds=5):
        still_running = ", ".join(str(process_id) for process_id in remaining if process_exists(process_id))
        raise RuntimeError(f"Output is still locked by running processes; build cannot continue: {still_running}")


def output_binary_path(binary_name: str, output_dir: Path) -> Path:
    suffix = ".exe" if sys.platform == "win32" else ""
    return output_dir / f"{binary_name}{suffix}"


def ensure_build_output_path(path: Path) -> None:
    resolved_build = BUILD_DIR.resolve()
    if not path.is_relative_to(resolved_build):
        raise ValueError(f"Refusing to stop a process outside the build directory: {path}")


def running_process_ids(binary_path: Path) -> list[int]:
    if sys.platform == "win32":
        return running_process_ids_windows(binary_path)

    if Path("/proc").exists():
        return running_process_ids_procfs(binary_path)

    return running_process_ids_ps(binary_path)


def running_process_ids_windows(binary_path: Path) -> list[int]:
    process_name = binary_path.name.replace("'", "''")
    command = [
        "powershell",
        "-NoProfile",
        "-Command",
        "$items = Get-CimInstance Win32_Process -Filter \"Name = '" + process_name + "'\" "
        "| Select-Object ProcessId, ExecutablePath; "
        "$items | ConvertTo-Json -Compress",
    ]
    # 进程路径按控制台代码页解码，严格模式遇到非法字节会让 stdout 变 None 并崩在 strip 上。
    result = subprocess.run(command, text=True, errors="replace", capture_output=True, check=False)
    text = result.stdout.strip()
    if not text:
        return []

    try:
        records = json.loads(text)
    except json.JSONDecodeError:
        return []

    if isinstance(records, dict):
        records = [records]

    process_ids: list[int] = []
    expected = normalized_path(binary_path)
    for record in records:
        if not isinstance(record, dict):
            continue
        path = record.get("ExecutablePath")
        process_id = record.get("ProcessId")
        if not isinstance(path, str) or process_id is None:
            continue
        if normalized_path(Path(path)) == expected:
            process_ids.append(int(process_id))

    return process_ids


def running_process_ids_procfs(binary_path: Path) -> list[int]:
    process_ids: list[int] = []
    expected = normalized_path(binary_path)
    current_pid = os.getpid()
    for entry in Path("/proc").iterdir():
        if not entry.name.isdigit():
            continue
        process_id = int(entry.name)
        if process_id == current_pid:
            continue
        try:
            process_path = Path(os.readlink(entry / "exe"))
        except (FileNotFoundError, PermissionError, OSError):
            continue
        if normalized_path(process_path) == expected:
            process_ids.append(process_id)

    return process_ids


def running_process_ids_ps(binary_path: Path) -> list[int]:
    result = subprocess.run(["ps", "-axo", "pid=,command="], text=True, errors="replace", capture_output=True, check=False)
    expected = normalized_path(binary_path)
    process_ids: list[int] = []
    for line in result.stdout.splitlines():
        text = line.strip()
        if not text:
            continue
        pid_text, _, command_text = text.partition(" ")
        if not pid_text.isdigit() or not command_text:
            continue
        try:
            command_path = Path(shlex.split(command_text)[0])
        except (IndexError, ValueError):
            continue
        if command_path.is_absolute() and normalized_path(command_path) == expected:
            process_ids.append(int(pid_text))

    return process_ids


def normalized_path(path: Path) -> str:
    resolved = str(path.resolve())
    return resolved.casefold() if sys.platform == "win32" else resolved


def terminate_process(process_id: int, force: bool) -> None:
    if sys.platform == "win32":
        command = ["taskkill", "/PID", str(process_id), "/T"]
        if force:
            command.append("/F")
        subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)
        return

    try:
        os.kill(process_id, 9 if force else 15)
    except ProcessLookupError:
        return


def wait_for_processes_exit(process_ids: list[int], timeout_seconds: float) -> bool:
    deadline = time.time() + timeout_seconds
    while time.time() < deadline:
        if all(not process_exists(process_id) for process_id in process_ids):
            return True
        time.sleep(0.1)

    return all(not process_exists(process_id) for process_id in process_ids)


def process_exists(process_id: int) -> bool:
    if sys.platform == "win32":
        result = subprocess.run(
            ["powershell", "-NoProfile", "-Command", f"Get-Process -Id {process_id} -ErrorAction SilentlyContinue"],
            text=True,
            errors="replace",
            capture_output=True,
            check=False,
        )
        return bool(result.stdout.strip())

    try:
        os.kill(process_id, 0)
        return True
    except ProcessLookupError:
        return False
