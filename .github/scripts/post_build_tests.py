#!/usr/bin/env python3
import argparse
import json
import os
import shutil
import socket
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))

sys.dont_write_bytecode = True
os.environ["PYTHONDONTWRITEBYTECODE"] = "1"
# 进度输出含 · 与 ✅，cp1252/cp936 控制台会抛 UnicodeEncodeError，故自行接管标准流编码。
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.stderr.reconfigure(encoding="utf-8", errors="replace")

from build_support.console import fail, header, print_summary, timing, warn

PORT = 20000
COMMAND_TIMEOUT_SECONDS = 180
MAX_STEPS = 50


class PostBuildTestError(RuntimeError):
    pass


class CommandFailure(PostBuildTestError):
    def __init__(self, command: str, response: str):
        super().__init__(f"{command} failed: {response}")
        self.command = command
        self.response = response


class PostBuildTests:
    def __init__(self, app_exec: Path, app_output: Path, os_family: str, shortcut_only: bool = False) -> None:
        self.app_exec = app_exec
        self.app_output = app_output
        self.os_family = os_family
        self.shortcut_only = shortcut_only
        self.env = os.environ.copy()
        if self.env.get("CI") == "true":
            self.env.setdefault("STELLIBERTY_DEBUG_SERVICE_CI", "1")
        self.log_dir = Path(self.env.get("RUNNER_TEMP", ROOT / "build" / "post-build-tests"))
        self.log_dir.mkdir(parents=True, exist_ok=True)
        self.app_log_path = self.log_dir / "stelliberty-post-build-app.log"
        self.xvfb_log_path = self.log_dir / "stelliberty-post-build-xvfb.log"
        self.app_running_log_path = self.app_output / "data" / "applogs" / "running.logs"
        self.app_process: subprocess.Popen | None = None
        self.xvfb_process: subprocess.Popen | None = None
        self.step_index = 0
        self.failed = False
        self.service_touched = False
        self.original_window_hotkey: str | None = None
        self.window_hotkey_changed = False
        if self.os_family == "linux" and not self.env.get("XDG_RUNTIME_DIR"):
            xdg_runtime_dir = self.log_dir / "xdg-runtime"
            xdg_runtime_dir.mkdir(mode=0o700, parents=True, exist_ok=True)
            os.chmod(xdg_runtime_dir, 0o700)
            self.env["XDG_RUNTIME_DIR"] = str(xdg_runtime_dir)

    def run(self) -> None:
        print(header(f"Post-build Tests / {self.os_family}"), flush=True)
        print(f"  App     {self.app_exec}", flush=True)
        print(f"  Output  {self.app_output}", flush=True)
        print()

        self.start_display()
        if self.shortcut_only:
            self.run_shortcut_steps()
        else:
            self.run_steps()
        print_summary(f"Passed {self.step_index}  Failed 0")

    def run_shortcut_steps(self) -> None:
        self.step("Start app and focus shortcut recorder", lambda: (
            self.start_app(),
            self.require("window.show"),
            self.prepare_window_shortcut(),
            self.open_page("settings/app-behavior", "Settings.WindowToggleHotkeyBox"),
            self.require("control.click Settings.WindowToggleHotkeyBox"),
        ))
        self.step("Suppress shortcut while recording", lambda: (
            self.require("hotkey.trigger window", contains=["action=ToggleWindow", "activated=false"]),
            self.require("window.state", contains=["visible=true"]),
        ))
        self.step("Toggle UI through the tray shortcut after recording", self.verify_window_shortcut)
        self.step("Restore shortcut setting", self.restore_window_shortcut)
        self.step("Close app after shortcut verification", self.stop_app_step)

    def prepare_window_shortcut(self) -> None:
        if self.os_family != "windows":
            return

        state = self.require("settings.app-behavior.state")
        self.original_window_hotkey = self.state_value(state, "windowToggleHotkey")
        temporary_hotkey = "Ctrl+Shift+F12"
        self.require(
            f"settings.app-behavior.set window-toggle-hotkey {temporary_hotkey}",
            contains=[f"windowToggleHotkey={temporary_hotkey}"],
        )
        self.window_hotkey_changed = self.original_window_hotkey != temporary_hotkey

    def restore_window_shortcut(self) -> None:
        if not self.window_hotkey_changed or not self.is_app_running():
            return

        value = self.original_window_hotkey or "__EMPTY__"
        self.require(
            f"settings.app-behavior.set window-toggle-hotkey {value}",
            contains=[f"windowToggleHotkey={self.original_window_hotkey or ''}"],
        )
        self.window_hotkey_changed = False

    @staticmethod
    def state_value(state: str, key: str) -> str:
        prefix = f"{key}="
        for item in state.split(";"):
            if item.startswith(prefix):
                return item[len(prefix):]
        raise PostBuildTestError(f"State does not contain {key!r}: {state!r}")

    def run_steps(self) -> None:
        self.step("Start app and verify window", lambda: (
            self.start_app(),
            self.require("window.show"),
            self.require("window.state", contains=["visible=true", "width=", "height="]),
            self.require("toast.state", contains=["visible=false"]),
        ))
        self.step("Verify process core mode", self.ensure_process_core_host)
        self.step("Home startup state", lambda: (
            self.require("page.open home"),
            self.wait_for("home.state", contains=["core=true", "coreHost=process", "serviceMode=NotInstalled", "restarting=false"]),
        ))
        self.step("Core status API", lambda: self.require("core.state", contains=['"state"']))
        self.step("Refresh home runtime metrics", lambda: self.require("home.refresh runtime", contains=["core=true", "uptime=", "connections="]))
        self.step("Refresh home network state", lambda: self.require("home.refresh network", contains=["network="]))
        self.step("Switch outbound mode to global", lambda: self.require("home.set outbound global", contains=["outbound=Global"]))
        self.step("Switch outbound mode to direct", lambda: self.require("home.set outbound direct", contains=["outbound=Direct"]))
        self.step("Restore outbound mode to rule", lambda: self.require("home.set outbound rule", contains=["outbound=Rule"]))
        self.step("Switch takeover tab to system proxy", lambda: self.require("home.select takeover proxy", contains=["takeover=proxy"]))
        self.step("Switch takeover tab to TUN", lambda: self.require("home.select takeover tun", contains=["takeover=tun"]))
        self.step("Verify TUN boolean input", lambda: (
            self.require_error("home.set tun off", contains=["Invalid boolean value: off"]),
            self.require("home.set tun false", contains=["tun=false"]),
        ))
        self.step("Reset traffic statistics", lambda: self.require("home.reset traffic", contains=["upload=", "download="]))
        self.step("Restart core and wait for recovery", lambda: (
            self.require("home.restart core"),
            self.wait_for("home.state", contains=["core=true", "restarting=false"]),
        ))

        self.step("Install service mode", self.install_service_mode)
        self.step("Verify service core after restart", lambda: (
            self.restart_app(),
            self.wait_for("home.state", contains=["core=true", "coreHost=service", "serviceMode=Running"]),
            self.wait_for("service.state", contains=["state=Running", "core=running"]),
            self.require("core.state", contains=['"state"']),
        ))
        self.step("Restart service core and wait for recovery", lambda: (
            self.require("home.restart core"),
            self.wait_for("home.state", contains=["core=true", "coreHost=service", "serviceMode=Running", "restarting=false"]),
            self.wait_for("service.state", contains=["state=Running", "core=running"]),
        ))
        self.step("Uninstall service mode and return to process core", self.uninstall_service_mode)

        self.step("Proxy page empty state", lambda: (
            self.open_page("proxies", "Proxy.Toolbar"),
            self.require("proxies.state", contains=["groups=", "nodes=", "testing=false"]),
        ))
        self.step("Refresh proxy page", lambda: self.require("proxies.refresh", contains=["groups=", "testing=false"]))
        self.step("Connection page initial state", lambda: (
            self.open_page("connections", "Connections.Toolbar"),
            self.require("connections.state", contains=["total=", "paused=false", "filter=All"]),
        ))
        self.step("Refresh connection list", lambda: self.require("connections.refresh", contains=["filter=All"]))
        self.step("Filter direct connections", lambda: self.require("connections.filter direct", contains=["filter=Direct"]))
        self.step("Filter proxy connections", lambda: self.require("connections.filter proxy", contains=["filter=Proxy"]))
        self.step("Restore all connection filter", lambda: self.require("connections.filter all", contains=["filter=All"]))
        self.step("Pause connection monitoring", lambda: self.require("connections.toggle pause", contains=["paused=true"]))
        self.step("Resume connection monitoring", lambda: self.require("connections.toggle pause", contains=["paused=false"]))
        self.step("Close all connections", lambda: self.require("connections.close all", contains=["closedAll=true"]))
        self.step("Core logs page initial state", lambda: (
            self.open_page("core-logs", "CoreLogs.Toolbar"),
            self.require("core-logs.state", contains=["running=true", "filter=All"]),
        ))
        self.step("Filter core logs by info", lambda: self.require("core-logs.filter info", contains=["filter=Info"]))
        self.step("Filter core logs by error", lambda: self.require("core-logs.filter error", contains=["filter=Error"]))
        self.step("Restore all core log filter", lambda: self.require("core-logs.filter all", contains=["filter=All"]))
        self.step("Pause core log monitoring", lambda: self.require("core-logs.toggle pause", contains=["paused=true"]))
        self.step("Resume core log monitoring", lambda: self.require("core-logs.toggle pause", contains=["paused=false"]))
        self.step("Clear core logs", lambda: self.require("core-logs.clear", contains=["running=true"]))
        self.step("Rules page initial state", lambda: (
            self.open_page("rules", "Rules.Toolbar"),
            self.require("rules.state", contains=["running=true", "bucket=All"]),
        ))
        self.step("Refresh rule list", lambda: self.require("rules.refresh", contains=["refresh=true"]))
        self.step("Filter domain rules", lambda: self.require("rules.filter domain", contains=["bucket=Domain"]))
        self.step("Filter IP rules", lambda: self.require("rules.filter ip", contains=["bucket=Ip"]))
        self.step("Filter rule-set rules", lambda: self.require("rules.filter rule-set", contains=["bucket=RuleSet"]))
        self.step("Filter other rules", lambda: self.require("rules.filter other", contains=["bucket=Other"]))
        self.step("Search rules after restoring all filter", lambda: (
            self.require("rules.filter all", contains=["bucket=All"]),
            self.require("rules.search example", contains=["search=example"]),
        ))
        self.step("Subscription page remains empty", lambda: (
            self.open_page("subscriptions", "Subscriptions.Toolbar"),
            self.require("control.exists Subscriptions.EmptyText"),
            self.require("subscriptions.state", contains=["total=0", "dialog=false"]),
            self.require("control.click Subscriptions.AddButton"),
            self.require("subscriptions.state", contains=["total=0", "dialog=true"]),
            self.wait_for("control.exists Subscriptions.AddDialog.CancelButton", contains=[], timeout=15, interval=0.1),
            self.require("control.click Subscriptions.AddDialog.CancelButton"),
            self.require("subscriptions.state", contains=["total=0", "dialog=false"]),
            self.require("subscriptions.state store", contains=["total=0", "remote=0", "local=0"]),
            self.require("subscriptions.state selection", contains=["exists=false"]),
            self.require("subscriptions.list", equals=""),
        ))
        self.step("Override page remains empty", lambda: (
            self.open_page("overrides", "Overrides.Toolbar"),
            self.require("control.exists Overrides.EmptyText"),
            self.require("overrides.state", contains=["total=0", "dialog=false"]),
            self.require("overrides.list", equals=""),
        ))
        self.step("Settings basic state", lambda: (
            self.open_page("settings/root", "Settings.Root"),
            self.require("settings.state", contains=["language=", "theme=", "proxyHost="]),
            self.require("settings.language.state", contains=["language="]),
            self.require("settings.theme.state", contains=["theme=", "windowEffect="]),
            self.require("settings.app-behavior.list keys", contains=["lazy-mode", "auto-start"]),
            self.require("settings.app-behavior.state", contains=["lazyMode=", "windowToggleHotkey=", "systemProxyToggleHotkey=", "tunToggleHotkey="]),
            self.require("settings.update.state", contains=["autoCheck=", "interval="]),
        ))
        self.step("Settings data management and WebDAV state", self.verify_webdav_settings)
        self.step("System integration and Clash settings state", lambda: (
            self.open_page("settings/system-integration", "Settings.PacModeToggle"),
            self.require("settings.system-integration.list keys", contains=["proxy-host", "pac-script"]),
            self.require("settings.system-integration.state", contains=["proxyHost=", "pacMode="]),
            self.require("clash.list keys", contains=["network.unified-delay", "core-log.level"]),
            self.require("clash.state", contains=["areas=", "apply=", "error="]),
        ))
        self.step("Close app and verify exit", self.stop_app_step)

    def ensure_process_core_host(self) -> None:
        self.uninstall_service_mode()

    def install_service_mode(self) -> None:
        self.service_touched = True
        self.require("service.install", contains=["result=Succeeded", "requiresRestart=false", "state=Running"])

    def uninstall_service_mode(self) -> None:
        self.require("service.uninstall", contains=["result=Succeeded", "requiresRestart=false", "state=NotInstalled"])
        self.service_touched = False
        self.wait_for("home.state", contains=["core=true", "coreHost=process", "serviceMode=NotInstalled"])
        self.wait_for("service.state", contains=["state=NotInstalled", "core=unknown"])

    def verify_webdav_settings(self) -> None:
        self.open_page("settings/data-management", "Settings.WebDavEnableToggle")
        self.require("settings.data-management.webdav.list keys", contains=["enabled", "url", "retention-count"])
        self.require("settings.data-management.webdav.state", contains=["webdavEnabled=", "webdavBusy=false"])
        self.require("settings.data-management.webdav.set enabled false", contains=["webdavEnabled=false"])
        self.require("settings.data-management.webdav.set url https://webdav.example/dav", contains=["webdavUrlSet=true"])
        self.require("settings.data-management.webdav.set username ci-user", contains=["webdavUserSet=true"])
        self.require("settings.data-management.webdav.set password ci-password", contains=["webdavUserSet=true"])
        self.require("settings.data-management.webdav.set remote-directory backups", contains=["webdavRemoteDirectory=backups"])
        self.require("settings.data-management.webdav.set interval-hours 24", contains=["webdavIntervalHours=24"])
        self.require("settings.data-management.webdav.set retention-count 3", contains=["webdavRetentionCount=3"])
        self.require("settings.data-management.webdav.set password __EMPTY__", contains=["webdavBusy=false"])
        self.require("settings.data-management.webdav.set username __EMPTY__", contains=["webdavUserSet=false"])
        self.require("settings.data-management.webdav.set url __EMPTY__", contains=["webdavUrlSet=false"])
        self.require("settings.data-management.webdav.set remote-directory stelliberty-backups", contains=["webdavRemoteDirectory=stelliberty-backups"])
        self.require("settings.data-management.webdav.set retention-count 5", contains=["webdavRetentionCount=5"])

    def stop_app_step(self) -> None:
        self.command("app.quit", allow_disconnect=True)
        self.wait_for_app_exit(timeout=15)
        if self.is_app_running():
            raise PostBuildTestError("App process is still running after the quit command returned")

    def open_page(self, page: str, ready_control: str) -> None:
        self.require(f"page.open {page}")
        self.wait_for(
            f"control.exists {ready_control}",
            contains=[],
            timeout=15,
            interval=0.1,
        )

    def step(self, label: str, action) -> None:
        self.step_index += 1
        if self.step_index > MAX_STEPS:
            raise PostBuildTestError(f"Post-build Tests exceeded {MAX_STEPS} steps")

        print(f"  [{self.step_index:02}] {label}", flush=True)
        started_at = time.perf_counter()
        try:
            action()
        except Exception:
            self.failed = True
            elapsed = time.perf_counter() - started_at
            print(f"       {fail('❌')} {timing(elapsed)}", flush=True)
            raise

        elapsed = time.perf_counter() - started_at
        print(f"       ✅ {timing(elapsed)}", flush=True)
        print()

    def require(
        self,
        command: str,
        *,
        contains: list[str] | None = None,
        equals: str | None = None,
    ) -> str:
        response = self.command(command)
        if equals is not None and response != equals:
            raise PostBuildTestError(f"{command} expected {equals!r}, actual {response!r}")

        for expected in contains or []:
            if expected not in response:
                raise PostBuildTestError(f"{command} missing expected fragment {expected!r}, actual {response!r}")

        return response

    def require_error(self, command: str, *, contains: list[str]) -> str:
        response = self.raw_command(command)
        if not response.startswith("ERR "):
            raise PostBuildTestError(f"{command} expected an error, actual {response!r}")

        for expected in contains:
            if expected not in response:
                raise PostBuildTestError(f"{command} missing expected fragment {expected!r}, actual {response!r}")

        self.print_command(command, response)
        return response

    def wait_for(self, command: str, *, contains: list[str], timeout: float = 60, interval: float = 1) -> str:
        deadline = time.time() + timeout
        last_response = ""
        last_error: Exception | None = None
        while time.time() < deadline:
            try:
                response = self.command(command, visible=False)
                last_response = response
                if all(expected in response for expected in contains):
                    self.print_command(command, response)
                    return response
            except Exception as exception:
                last_error = exception
            time.sleep(interval)

        if last_error is not None:
            raise PostBuildTestError(f"{command} wait failed: {last_error}") from last_error
        raise PostBuildTestError(f"{command} did not contain {contains!r} within {timeout:.0f}s, last response {last_response!r}")

    def command(self, command: str, *, visible: bool = True, allow_disconnect: bool = False) -> str:
        try:
            raw_response = self.raw_command(command)
        except Exception:
            if allow_disconnect:
                if visible:
                    self.print_command(command, "")
                return ""
            raise

        if raw_response == "OK":
            response = ""
        elif raw_response.startswith("OK "):
            response = raw_response[3:]
        else:
            raise CommandFailure(command, raw_response)

        if visible:
            self.print_command(command, response)
        return response

    def raw_command(self, command: str) -> str:
        with socket.create_connection(("127.0.0.1", PORT), timeout=10) as client:
            client.settimeout(COMMAND_TIMEOUT_SECONDS)
            client.sendall((command + "\n").encode("utf-8"))
            return client.makefile("r", encoding="utf-8").read().rstrip("\n")

    def print_command(self, command: str, response: str) -> None:
        output = response.replace("\n", "\\n")
        if len(output) > 500:
            output = output[:497] + "..."
        print(f"       {command} => {output}", flush=True)

    def start_display(self) -> None:
        if self.os_family != "linux" or self.env.get("DISPLAY"):
            return

        xvfb = shutil.which("Xvfb")
        if xvfb is None:
            raise PostBuildTestError("Linux Post-build Tests require Xvfb, but it was not found in the current environment")

        display = self.next_xvfb_display()
        with self.xvfb_log_path.open("w", encoding="utf-8") as log:
            self.xvfb_process = subprocess.Popen(
                [xvfb, display, "-screen", "0", "1280x720x24"],
                stdout=log,
                stderr=subprocess.STDOUT,
                env=self.env,
            )
        self.env["DISPLAY"] = display
        time.sleep(0.5)
        if self.xvfb_process.poll() is not None:
            raise PostBuildTestError("Xvfb failed to start")

    def next_xvfb_display(self) -> str:
        for number in range(99, 110):
            if not Path(f"/tmp/.X11-unix/X{number}").exists():
                return f":{number}"
        raise PostBuildTestError("No available Xvfb display number")

    def start_app(self) -> None:
        if self.is_app_running():
            return

        if not self.app_exec.exists():
            raise PostBuildTestError(f"App entry point does not exist: {self.app_exec}")

        if self.try_probe_port():
            raise PostBuildTestError(f"Debug port {PORT} is already in use; refusing to reuse an unknown app")

        with self.app_log_path.open("w", encoding="utf-8") as log:
            kwargs: dict = {
                "cwd": self.app_output,
                "env": self.env,
                "stdin": subprocess.DEVNULL,
                "stdout": log,
                "stderr": subprocess.STDOUT,
            }
            if self.os_family == "windows":
                kwargs["creationflags"] = getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0)
            else:
                kwargs["start_new_session"] = True
            self.app_process = subprocess.Popen([str(self.app_exec), "--show-ui"], **kwargs)

        self.wait_debug_ready()

    def restart_app(self) -> None:
        self.stop_app()
        self.start_app()

    def stop_app(self) -> None:
        if not self.is_app_running():
            self.app_process = None
            return

        try:
            self.tray_command("stop")
            self.wait_for_app_exit(timeout=15)
        except Exception:
            pass

        if not self.is_app_running():
            self.app_process = None
            return

        self.app_process.terminate()
        self.wait_for_app_exit(timeout=5)
        if self.is_app_running():
            self.app_process.kill()
            self.wait_for_app_exit(timeout=5)
        self.app_process = None

    def wait_debug_ready(self) -> None:
        deadline = time.time() + 60
        while time.time() < deadline:
            if self.app_process is not None and self.app_process.poll() is not None:
                raise PostBuildTestError(f"App exited during startup with code {self.app_process.returncode}")
            if self.try_probe_port():
                return
            time.sleep(0.5)
        raise PostBuildTestError("Debug port was not ready within 60s")

    def tray_command(self, command: str):
        result = subprocess.run(
            [str(self.app_exec), "--debug-command", command], cwd=self.app_output, env=self.env,
            capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=15,
            creationflags=subprocess.CREATE_NO_WINDOW if self.os_family == "windows" else 0)
        if result.returncode:
            raise PostBuildTestError(result.stderr.strip() or "Tray command failed")
        return json.loads(result.stdout)

    def wait_ui_closed(self) -> dict:
        # 隐藏回收等待 60 秒，另留 15 秒完成会话注销。
        deadline = time.monotonic() + 75
        while time.monotonic() < deadline:
            state = self.tray_command("state")
            if state["UiPid"] is None:
                return state
            time.sleep(0.2)
        raise PostBuildTestError("UI session remained registered after closing the window")

    def verify_window_shortcut(self) -> None:
        self.require("control.click Navigation.HomeButton")
        time.sleep(0.6)
        lightweight_mode = self.state_value(self.require("settings.app-behavior.state"), "lightweightMode") == "true"
        before = self.tray_command("state")
        self.command("hotkey.trigger window", allow_disconnect=True)
        self.wait_for("window.state", contains=["visible=false"], timeout=15, interval=0.1)
        # 连续触发需越过快捷键的 500 毫秒防重复间隔。
        time.sleep(0.6)
        if not self.tray_command("toggle-window"):
            raise PostBuildTestError("The tray shortcut could not reopen the UI before release")
        self.wait_for("window.state", contains=["visible=true"], timeout=15, interval=0.1)
        reopened = self.tray_command("state")
        if before["UiPid"] != reopened["UiPid"]:
            raise PostBuildTestError("Reopening before the hidden delay did not reuse the UI process")
        time.sleep(0.6)
        self.command("hotkey.trigger window", allow_disconnect=True)
        if lightweight_mode:
            self.wait_ui_closed()
        else:
            self.wait_for("window.state", contains=["visible=false"], timeout=15, interval=0.1)
        time.sleep(0.6)
        if not self.tray_command("toggle-window"):
            raise PostBuildTestError("The tray shortcut could not reopen the UI")
        self.wait_debug_ready()
        after = self.tray_command("state")
        ui_recreated = before["UiPid"] != after["UiPid"]
        if before["TrayPid"] != after["TrayPid"] or ui_recreated != lightweight_mode:
            raise PostBuildTestError("The shortcut did not follow the lightweight mode preference")
        if before["Core"]["Snapshot"]["Pid"] != after["Core"]["Snapshot"]["Pid"]:
            raise PostBuildTestError("Hiding the UI unexpectedly restarted the core")

    def try_probe_port(self) -> bool:
        try:
            raw = self.raw_command("window.state")
            return raw.startswith("OK")
        except Exception:
            return False

    def wait_for_app_exit(self, timeout: float) -> None:
        if self.app_process is None:
            return
        try:
            self.app_process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            return

    def is_app_running(self) -> bool:
        return self.app_process is not None and self.app_process.poll() is None

    def cleanup(self) -> None:
        if self.window_hotkey_changed:
            try:
                self.restore_window_shortcut()
            except Exception as exception:
                print(f"  {warn('Shortcut setting cleanup failed')} {exception}", flush=True)

        if self.service_touched:
            try:
                if not self.is_app_running():
                    self.start_app()
                self.command("service.uninstall", visible=False)
                self.service_touched = False
            except Exception as exception:
                print(f"  {warn('Service mode cleanup failed')} {exception}", flush=True)

        self.stop_app()
        if self.xvfb_process is not None and self.xvfb_process.poll() is None:
            self.xvfb_process.terminate()
            try:
                self.xvfb_process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                self.xvfb_process.kill()

    def diagnostics(self) -> str:
        parts: list[str] = []
        if self.app_log_path.exists():
            parts.append(format_log_excerpt("App output", self.app_log_path))
        if self.app_running_log_path.exists():
            parts.append(format_log_excerpt("App internal log", self.app_running_log_path))
        if self.xvfb_log_path.exists():
            parts.append(format_log_excerpt("Xvfb output", self.xvfb_log_path))
        return "\n".join(part for part in parts if part)


def format_log_excerpt(title: str, path: Path, lines: int = 80) -> str:
    text = path.read_text(encoding="utf-8", errors="replace").strip()
    if not text:
        return ""
    tail = "\n".join(text.splitlines()[-lines:])
    return f"--- {title}: last {lines} lines ---\n{tail}"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Run Post-build Tests against a packaged Debug app")
    parser.add_argument("--app-exec", type=Path, required=True, help="Built Debug app executable path")
    parser.add_argument("--app-output", type=Path, help="Built Debug app output directory")
    parser.add_argument("--os-family", choices=["windows", "linux", "macos"], default=detect_os_family())
    parser.add_argument("--shortcut-only", action="store_true", help="Run only shortcut trigger tests")
    return parser.parse_args()


def detect_os_family() -> str:
    if sys.platform == "win32":
        return "windows"
    if sys.platform == "darwin":
        return "macos"
    return "linux"


def main() -> int:
    args = parse_args()
    app_exec = args.app_exec.resolve()
    app_output = (args.app_output or app_exec.parent).resolve()
    runner = PostBuildTests(app_exec, app_output, args.os_family, shortcut_only=args.shortcut_only)
    try:
        runner.run()
        return 0
    except Exception as exception:
        print()
        print(fail(f"Post-build Tests failed: {exception}"), flush=True)
        diagnostics = runner.diagnostics()
        if diagnostics:
            print(diagnostics, flush=True)
        print_summary(f"Passed {max(0, runner.step_index - 1)}  Failed 1")
        return 1
    finally:
        runner.cleanup()


if __name__ == "__main__":
    sys.exit(main())
