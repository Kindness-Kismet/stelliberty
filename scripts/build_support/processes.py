import json
import os
import shlex
import subprocess
import sys
from pathlib import Path

from build_support.models import AppMetadata
from build_support.paths import BUILD_DIR, DEPS_DIRECTORY, ROOT


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

    skill_paths = [ROOT / directory / "skills" / "app-debug" / "scripts" / "cli.py" for directory in (".codex", ".claude")]
    cli_path = next((path for path in skill_paths if path.is_file()), None)
    if cli_path is None:
        raise RuntimeError(f"Close the running output app before building; app-debug is unavailable: {resolved_output}")

    print(f"  Stopping output app through app-debug: {resolved_output}", flush=True)
    env = os.environ.copy()
    env["STELLIBERTY_DEBUG_ROOT"] = str(ROOT)
    subprocess.run([sys.executable, str(cli_path), "app.stop", "--output-dir", str(resolved_output)], cwd=ROOT, env=env, check=True)


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
