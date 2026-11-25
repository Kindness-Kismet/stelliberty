import json
import os
import shlex
import signal
import socket
import subprocess
import sys
import time
from pathlib import Path
from shutil import which as shutil_which

from build_support.models import AppMetadata
from build_support.paths import BUILD_DIR, DEPS_DIRECTORY


def close_running_output_app(metadata: AppMetadata, output_dir: Path) -> None:
    resolved_output = output_dir.resolve()
    ensure_build_output_path(resolved_output)
    ui_name = f"{metadata.app_name}_ui"
    binary_paths = [
        output_binary_path(metadata.app_name, resolved_output),
        output_binary_path(ui_name, resolved_output / DEPS_DIRECTORY),
        output_binary_path(ui_name, resolved_output),
    ]
    process_ids = [
        process_id
        for binary_path in binary_paths
        if binary_path.is_file()
        for process_id in running_process_ids(binary_path)
    ]
    if not process_ids:
        return

    print(f"  Stopping exact output app: {resolved_output}", flush=True)
    stop_running_paths(set(binary_paths))


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
    command[-1] = "[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new(); " + command[-1]
    result = subprocess.run(command, text=True, encoding="utf-8", errors="replace", capture_output=True, check=True, creationflags=subprocess.CREATE_NO_WINDOW)
    text = result.stdout.strip()
    if not text:
        return []

    try:
        records = json.loads(text)
    except json.JSONDecodeError as error:
        raise RuntimeError("Process enumeration returned invalid JSON.") from error

    if records is None:
        return []
    if isinstance(records, dict):
        records = [records]
    if not isinstance(records, list):
        raise RuntimeError("Process enumeration returned an invalid record collection.")

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


def process_binary_path(pid: int) -> Path | None:
    if sys.platform == "win32":
        script = "[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new(); " + f"(Get-Process -Id {pid} -ErrorAction SilentlyContinue).Path"
        result = subprocess.run(["powershell", "-NoProfile", "-Command", script], capture_output=True, text=True,
                                encoding="utf-8", errors="replace", creationflags=subprocess.CREATE_NO_WINDOW)
        value = result.stdout.strip()
        return Path(value) if value else None
    process_path = Path(f"/proc/{pid}/exe")
    if process_path.exists():
        return Path(os.readlink(process_path))
    result = subprocess.run(["ps", "-p", str(pid), "-o", "command="], capture_output=True, text=True, errors="replace")
    values = shlex.split(result.stdout.strip())
    return Path(values[0]) if values and Path(values[0]).is_absolute() else None


def listening_process_ids(port: int) -> list[int]:
    if sys.platform == "win32":
        script = f"Get-NetTCPConnection -LocalAddress 127.0.0.1 -LocalPort {port} -State Listen -ErrorAction SilentlyContinue | ForEach-Object {{ $_.OwningProcess }} | Sort-Object -Unique"
        result = subprocess.run(["powershell", "-NoProfile", "-Command", script], capture_output=True, text=True,
                                errors="replace", creationflags=subprocess.CREATE_NO_WINDOW)
        return [int(line) for line in result.stdout.splitlines() if line.strip().isdigit()]
    if shutil_which("lsof"):
        result = subprocess.run(["lsof", "-nP", f"-iTCP@127.0.0.1:{port}", "-sTCP:LISTEN", "-t"], capture_output=True, text=True)
        return [int(line) for line in result.stdout.splitlines() if line.strip().isdigit()]
    if shutil_which("ss"):
        result = subprocess.run(["ss", "-ltnp"], capture_output=True, text=True)
        import re

        pattern = re.compile(rf"127\.0\.0\.1:{port}\b.*pid=(\d+)")
        return [int(match[1]) for line in result.stdout.splitlines() if (match := pattern.search(line))]
    raise RuntimeError("Install lsof or ss to identify the loopback Debug port owner.")


def terminate_exact_process(pid: int, path: Path) -> None:
    # 结束前复核可执行路径，避免 PID 复用触及其他应用。
    actual = process_binary_path(pid)
    if actual is None:
        return
    if normalized_path(actual) != normalized_path(path):
        raise RuntimeError(f"PID {pid} changed executable identity; refusing to stop it.")
    if sys.platform != "win32":
        os.kill(pid, signal.SIGKILL)
        return
    result = subprocess.run(["taskkill", "/PID", str(pid), "/F"], capture_output=True,
                            text=True, errors="replace", creationflags=subprocess.CREATE_NO_WINDOW)
    if result.returncode and pid in running_process_ids(path):
        raise RuntimeError(f"Stopping output PID {pid} failed (exit code {result.returncode}).")


def stop_running_paths(binary_paths: set[Path], port: int = 20000) -> None:
    expected = {path.resolve() for path in binary_paths}
    targets = {pid for path in expected for pid in running_process_ids(path)}
    if not targets:
        return
    if targets.intersection(listening_process_ids(port)):
        try:
            with socket.create_connection(("127.0.0.1", port), timeout=10) as client:
                client.sendall(b"app.quit\n")
                with client.makefile("rb") as response:
                    data = response.read()
            if data.startswith(b"ERR"):
                raise RuntimeError("The target app rejected the shutdown request.")
        except (OSError, RuntimeError) as error:
            detail = str(error) if isinstance(error, RuntimeError) else f"{type(error).__name__}; errno={error.errno}"
            print(f"  warning: Normal output-app shutdown failed ({detail}).", file=sys.stderr)
        deadline = time.monotonic() + 10
        while time.monotonic() < deadline:
            if not any(running_process_ids(path) for path in expected):
                return
            time.sleep(0.1)
    for path in expected:
        for pid in running_process_ids(path):
            terminate_exact_process(pid, path)
    deadline = time.monotonic() + 10
    while time.monotonic() < deadline:
        if not any(running_process_ids(path) for path in expected):
            return
        time.sleep(0.1)
    raise TimeoutError("The exact output app did not exit within the shutdown budget.")
