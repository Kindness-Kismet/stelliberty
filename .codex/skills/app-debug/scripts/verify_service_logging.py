#!/usr/bin/env python3
"""在隔离目录验证调试服务的文件日志，不安装系统服务。"""
import sys

sys.dont_write_bytecode = True

import os

os.environ["PYTHONDONTWRITEBYTECODE"] = "1"

import argparse
import ctypes
import errno
import hashlib
import json
import re
import shutil
import socket
import struct
import subprocess
import sys
import time
import uuid
from datetime import datetime
from pathlib import Path


MAX_LOG_BYTES = 4 * 1024 * 1024
START_MARKER = "Foreground service started"
STOP_MARKER = "Foreground service stopped"


def service_token() -> str:
    name, _ = app_debug.read_app_metadata()
    return re.sub(r"[^a-z0-9]+", "_", name.lower()).strip("_") or "app"


def endpoint_name() -> str:
    name = f"{service_token()}_service_dev"
    return rf"\\.\pipe\{name}" if sys.platform == "win32" else f"/tmp/{name}.sock"


def endpoint_owner(endpoint: str | None = None) -> int | None:
    endpoint = endpoint or endpoint_name()
    if sys.platform == "win32":
        from ctypes import wintypes

        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD,
                                     wintypes.LPVOID, wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
        kernel.CreateFileW.restype = wintypes.HANDLE
        kernel.GetNamedPipeServerProcessId.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.ULONG)]
        kernel.GetNamedPipeServerProcessId.restype = wintypes.BOOL
        kernel.CloseHandle.argtypes = [wintypes.HANDLE]
        kernel.CloseHandle.restype = wintypes.BOOL
        # 只连接明确的调试管道，使用系统返回的服务端 PID 核对本脚本子进程。
        handle = kernel.CreateFileW(endpoint, 0xC0000000, 0, None, 3, 0, None)
        if handle == ctypes.c_void_p(-1).value:
            error = ctypes.get_last_error()
            if error == 2:
                return None
            raise OSError(error, "Debug service pipe is unavailable; verification stopped")
        try:
            owner = wintypes.ULONG()
            if not kernel.GetNamedPipeServerProcessId(handle, ctypes.byref(owner)):
                raise ctypes.WinError(ctypes.get_last_error())
            return owner.value
        finally:
            kernel.CloseHandle(handle)

    with socket.socket(socket.AF_UNIX, socket.SOCK_STREAM) as client:
        client.settimeout(1)
        result = client.connect_ex(endpoint)
        if result in (errno.ENOENT, errno.ECONNREFUSED):
            return None
        if result != 0:
            raise OSError(result, "Debug service socket is unavailable; verification stopped")
        if sys.platform == "linux":
            return struct.unpack("3i", client.getsockopt(socket.SOL_SOCKET, socket.SO_PEERCRED, 12))[0]
        if sys.platform == "darwin":
            # Darwin 的 SOL_LOCAL=0、LOCAL_PEERPID=2 返回对端进程编号。
            return struct.unpack("i", client.getsockopt(0, 2, 4))[0]
        raise RuntimeError("Service endpoint ownership verification is unsupported on this platform")


def require_idle_endpoint() -> None:
    owner = endpoint_owner()
    if owner is not None:
        raise RuntimeError(f"Debug service endpoint is owned by PID {owner}; no service was started or stopped")


def require_owned_endpoint(process: subprocess.Popen) -> None:
    if process.poll() is not None:
        raise RuntimeError(f"Isolated service exited with code {process.returncode}")
    if endpoint_owner() != process.pid:
        raise RuntimeError("Debug service endpoint does not belong to this verification process")


def process_options() -> dict:
    options = {"stdin": subprocess.DEVNULL}
    if sys.platform == "win32":
        options["creationflags"] = subprocess.CREATE_NO_WINDOW
    else:
        options["start_new_session"] = True
    return options


class OwnedWindowsCore:
    def __init__(self, pid: int, binary: Path):
        from ctypes import wintypes

        self.pid = pid
        self.kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        self.kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
        self.kernel.OpenProcess.restype = wintypes.HANDLE
        self.kernel.QueryFullProcessImageNameW.argtypes = [wintypes.HANDLE, wintypes.DWORD,
                                                        wintypes.LPWSTR, ctypes.POINTER(wintypes.DWORD)]
        self.kernel.QueryFullProcessImageNameW.restype = wintypes.BOOL
        self.kernel.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
        self.kernel.WaitForSingleObject.restype = wintypes.DWORD
        self.kernel.TerminateProcess.argtypes = [wintypes.HANDLE, wintypes.UINT]
        self.kernel.TerminateProcess.restype = wintypes.BOOL
        self.kernel.CloseHandle.argtypes = [wintypes.HANDLE]
        self.kernel.CloseHandle.restype = wintypes.BOOL
        # 仅申请等待、路径查询和异常清理权限；句柄须对应隔离目录中的核心。
        self.handle = self.kernel.OpenProcess(0x00101001, False, pid)
        if not self.handle:
            raise ctypes.WinError(ctypes.get_last_error())
        try:
            size = wintypes.DWORD(32768)
            path = ctypes.create_unicode_buffer(size.value)
            if not self.kernel.QueryFullProcessImageNameW(self.handle, 0, path, ctypes.byref(size)):
                raise ctypes.WinError(ctypes.get_last_error())
            if Path(path.value).resolve() != binary.resolve():
                raise RuntimeError("Service reported a core process outside the isolated directory")
        except Exception:
            self.close()
            raise

    def is_running(self) -> bool:
        result = self.kernel.WaitForSingleObject(self.handle, 0)
        if result == 0xFFFFFFFF:
            raise ctypes.WinError(ctypes.get_last_error())
        return result == 0x00000102

    def wait_for_controller(self, controller: str) -> None:
        deadline = time.monotonic() + 15
        while time.monotonic() < deadline:
            if not self.is_running():
                raise RuntimeError("Isolated core exited before its controller became ready")
            owner = endpoint_owner(controller)
            if owner is not None:
                if owner != self.pid:
                    raise RuntimeError("Isolated core controller does not belong to the started core process")
                return
            time.sleep(0.1)
        raise TimeoutError("Isolated core controller startup timed out")

    def wait_for_exit(self) -> None:
        if self.kernel.WaitForSingleObject(self.handle, 5000) != 0:
            raise RuntimeError("Isolated core remained running after service shutdown")

    def terminate(self) -> None:
        if not self.kernel.TerminateProcess(self.handle, 1):
            raise ctypes.WinError(ctypes.get_last_error())
        self.wait_for_exit()

    def close(self) -> None:
        self.kernel.CloseHandle(self.handle)


def prepare_core(directory: Path) -> tuple[dict, str]:
    data = directory / "data"
    service = data / "service"
    core = data / "core"
    service.mkdir(parents=True, exist_ok=False)
    core.mkdir(exist_ok=False)
    source = app_debug.debug_dir() / "data" / "core" / "clash-mihomo-core.exe"
    binary = core / source.name
    shutil.copy2(source, binary)
    controller = rf"\\.\pipe\{service_token()}_service_logs_{uuid.uuid4().hex}"
    config = core / "service-log-verification.yaml"
    config.write_text("\n".join([
        "mixed-port: 0", "port: 0", "socks-port: 0", "redir-port: 0", "tproxy-port: 0",
        "allow-lan: false", "mode: direct", "log-level: info", "ipv6: false",
        "external-controller: ''", f"external-controller-pipe: {json.dumps(controller)}",
        "dns:", "  enable: false", "tun:", "  enable: false", "geo-auto-update: false",
        "profile:", "  store-selected: false", "  store-fake-ip: false",
        "proxies: []", "proxy-groups: []", "rules: []", "",
    ]), encoding="utf-8")
    # 服务按最近的 data 祖先定位核心和孤儿锁，整个布局必须位于本次新目录。
    if not service.resolve().is_relative_to(directory.resolve()) or any(service.iterdir()):
        raise RuntimeError("Isolated service lock directory must be newly created and empty")
    return {
        "type": "StartCore",
        "data": {"mihomo_path": str(binary), "config_path": str(config), "data_core_dir": str(core)},
    }, controller


class ServiceLogVerification:
    def __init__(self, directory: Path, binary: Path, core_request: dict | None = None, controller: str | None = None):
        self.directory = directory
        self.binary = binary
        self.log = directory / "service.logs"
        self.previous = directory / "service.previous.logs"
        self.events: list[dict] = []
        self.core_request = core_request
        self.controller = controller

    def command(self, name: str, *arguments: str, process: subprocess.Popen | None = None,
                input_payload: dict | None = None) -> str:
        if process is not None:
            require_owned_endpoint(process)
        else:
            require_idle_endpoint()
        options = process_options()
        if input_payload is not None:
            options.pop("stdin")
        result = subprocess.run([str(self.binary), name, *arguments], cwd=self.directory,
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                text=True, encoding="utf-8", errors="replace", timeout=15,
                                input=json.dumps(input_payload) if input_payload is not None else None,
                                **options)
        path = self.directory / f"command-{len(self.events):02d}-{name}.logs"
        path.write_text(result.stdout + result.stderr, encoding="utf-8")
        self.events.append({"command": name, "exit_code": result.returncode, "output": str(path)})
        if result.returncode != 0:
            raise RuntimeError(f"Service command {name} failed with exit code {result.returncode}; see {path}")
        return result.stdout

    def run_cycle(self, name: str, with_core: bool = False) -> bytes:
        require_idle_endpoint()
        path = self.directory / f"{name}-console.logs"
        with path.open("xb") as output:
            process = subprocess.Popen([str(self.binary), "foreground"], cwd=self.directory,
                                       stdout=output, stderr=subprocess.STDOUT, **process_options())
            event = {"cycle": name, "pid": process.pid, "console": str(path)}
            self.events.append(event)
            core = None
            try:
                deadline = time.monotonic() + 15
                while time.monotonic() < deadline:
                    if process.poll() is not None:
                        raise RuntimeError(f"Isolated service startup failed with exit code {process.returncode}; see {path}")
                    owner = endpoint_owner()
                    if owner is not None:
                        if owner != process.pid:
                            raise RuntimeError("Debug endpoint belongs to another process; verification stopped")
                        break
                    time.sleep(0.1)
                else:
                    raise TimeoutError(f"Isolated service startup timed out; see {path}")

                live_logs = self.command("logs", "2000", process=process)
                if START_MARKER not in live_logs:
                    raise RuntimeError("Running service logs are missing the startup marker")
                if with_core:
                    started = self.command("start-core", process=process, input_payload=self.core_request)
                    match = re.search(r"\bcore started pid=(\d+)\b", started)
                    if match is None:
                        raise RuntimeError("Service did not report the new core process ID")
                    core = OwnedWindowsCore(int(match.group(1)), Path(self.core_request["data"]["mihomo_path"]))
                    event["core_pid"] = core.pid
                    core.wait_for_controller(self.controller)
                self.command("shutdown", process=process)
                if process.wait(timeout=15) != 0:
                    raise RuntimeError(f"Isolated service exited unexpectedly with code {process.returncode}")
                if core is not None:
                    core.wait_for_exit()
                    event["core_exited"] = True
            finally:
                if process.poll() is None:
                    # 仅清理本脚本持有句柄的子进程，不按名称或外部 PID 结束进程。
                    process.terminate()
                    try:
                        process.wait(timeout=5)
                    except subprocess.TimeoutExpired:
                        process.kill()
                        process.wait(timeout=5)
                    event["forced_cleanup"] = True
                event["exit_code"] = process.returncode
                if core is not None:
                    try:
                        if core.is_running():
                            core.terminate()
                            event["core_forced_cleanup"] = True
                    finally:
                        core.close()

        offline_logs = self.command("logs", "2000")
        if START_MARKER not in offline_logs or STOP_MARKER not in offline_logs:
            raise RuntimeError("Offline service logs are missing startup or shutdown markers")
        if self.command("logs", "0").strip() != "no logs":
            raise RuntimeError("Offline logs 0 did not return 'no logs'")
        if with_core:
            core_pid = event["core_pid"]
            if f"Core process stop started: pid={core_pid}" not in offline_logs:
                raise RuntimeError("Service logs are missing the core shutdown start marker")
            pattern = rf"Core process exit confirmed: pid={core_pid}\b[^\r\n]*exit_code=0x[0-9A-Fa-f]{{8}}\b"
            if re.search(pattern, offline_logs) is None:
                raise RuntimeError("Service logs are missing core exit confirmation or a hexadecimal exit code")
        data = self.log.read_bytes()
        if b"Running service command: logs" in data or b"Running service command: shutdown" in data:
            raise RuntimeError("Client commands wrote to the service host log")
        if self.log.stat().st_size > MAX_LOG_BYTES:
            raise RuntimeError("Current log exceeds the rotation limit")
        return data

    def fill_for_rotation(self, label: str) -> str:
        require_idle_endpoint()
        marker = f"service-log-verification-{label}\n".encode()
        with self.log.open("ab") as output:
            while output.tell() <= MAX_LOG_BYTES:
                output.write(marker * 1024)
        return hashlib.sha256(self.log.read_bytes()).hexdigest()

    def run(self) -> None:
        first = self.run_cycle("first-start", with_core=self.core_request is not None)
        second = self.run_cycle("second-start")
        if not second.startswith(first) or second.count(START_MARKER.encode()) != 2:
            raise RuntimeError("Service restart did not append to the existing log")
        for index in (1, 2):
            expected = self.fill_for_rotation(f"rotation-{index}")
            self.run_cycle(f"rotation-{index}")
            if not self.previous.is_file() or hashlib.sha256(self.previous.read_bytes()).hexdigest() != expected:
                raise RuntimeError("Rotated log does not match the pre-rotation contents")
            backups = list(self.directory.glob("service.previous*"))
            if backups != [self.previous]:
                raise RuntimeError("Log rotation created additional previous-log files")


def main() -> int:
    global app_debug

    parser = argparse.ArgumentParser(description="Verify file logging in an isolated Debug service without installing a system service.")
    parser.add_argument("--record-dir", type=Path, required=True, help="Task attachment directory under this project's build/record.")
    parser.add_argument("--with-core", action="store_true", help="Start an isolated Windows core in the first cycle and verify its exit logs.")
    if len(sys.argv) == 1:
        parser.print_help()
        return 0
    args = parser.parse_args()
    import runtime as app_debug
    if args.with_core and sys.platform != "win32":
        raise ValueError("Real core exit-code verification requires Windows; basic log verification is cross-platform")
    base = args.record_dir.resolve()
    if not base.is_relative_to((app_debug.ROOT / "build" / "record").resolve()):
        raise ValueError("Verification attachments must be inside this project's build/record directory")
    require_idle_endpoint()
    suffix = ".exe" if sys.platform == "win32" else ""
    source = app_debug.debug_dir() / "data" / "service" / "update" / f"{service_token()}_service{suffix}"
    if not source.is_file():
        raise FileNotFoundError(f"Debug service output is missing; build the app first: {source}")
    directory = base / "isolated-service" / datetime.now().strftime("%Y%m%d-%H%M%S-%f")
    directory.mkdir(parents=True, exist_ok=False)
    core_request, controller = prepare_core(directory) if args.with_core else (None, None)
    service_directory = directory / "data" / "service" if args.with_core else directory
    binary = service_directory / source.name
    shutil.copy2(source, binary)
    verification = ServiceLogVerification(service_directory, binary, core_request, controller)
    summary = {"source": str(source), "directory": str(directory), "endpoint": endpoint_name(),
               "with_core": args.with_core, "controller": controller}
    code = 0
    try:
        verification.run()
        summary["result"] = "通过"
    except Exception as error:
        detail = (f"File or platform operation failed ({type(error).__name__}; errno={error.errno}; winerror={getattr(error, 'winerror', None)}; filename={error.filename})."
                  if isinstance(error, OSError) else str(error))
        summary.update(result="失败", error=detail)
        code = 1
    finally:
        summary["events"] = verification.events
        result_path = directory / "verification.json"
        result_path.write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
        print(json.dumps({"result": summary["result"], "exit_code": code, "record": str(result_path)}, ensure_ascii=False))
    return code


if __name__ == "__main__":
    sys.exit(main())
