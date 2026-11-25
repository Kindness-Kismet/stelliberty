import argparse
from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path


class CommandFailure(RuntimeError):
    def __init__(self, message: str, exit_code: int = 1):
        super().__init__(message)
        self.exit_code = exit_code


@dataclass(frozen=True)
class Command:
    name: str
    summary: str
    handler: str
    configure: Callable[[argparse.ArgumentParser], None] | None = None
    examples: tuple[str, ...] = ()
    target_fields: tuple[str, ...] = ()
    supports_no_focus: bool = False
    supports_focus_mode: bool = False


def add_focus_arguments(
    parser: argparse.ArgumentParser, *,
    supports_no_focus: bool, supports_focus_mode: bool,
    default=argparse.SUPPRESS,
) -> None:
    group = parser.add_mutually_exclusive_group()
    if supports_no_focus:
        group.add_argument(
            "--no-focus", dest="launch_no_focus", action="store_true", default=default,
            help="Start a new app instance without activation; use for Debug skill launches.",
        )
    if supports_focus_mode:
        group.add_argument(
            "--focus-mode", choices=("focus", "no-focus"), default=default,
            help="Set the running app's focus policy without activating its windows.",
        )


def focus_launch_arguments(launch_no_focus: bool) -> tuple[str, ...]:
    return ("--no-focus",) if launch_no_focus else ()


# 参数配置只依赖本地声明，实际执行由 run.py 延迟加载。

BOOLEAN_CHOICES = ("true", "false")
NO_FOCUS_COMMANDS = {"app.start", "app.ensure", "app.restart"}
FOCUS_MODE_COMMANDS = {"app.start", "app.ensure", "window.show", "window.close", "window.move", "window.resize",
                       "window.minimize", "window.maximize", "window.restore"}


class CommandDefinition:
    def __init__(self, name: str, summary: str):
        self.name = name
        self.summary = summary
        self.arguments = []

    def add_argument(self, *names, **options):
        if "help" not in options:
            options["help"] = "Value for " + names[0].lstrip("-").replace("_", " ") + "."
        self.arguments.append((names, options))

    def configure(self, parser):
        for names, options in self.arguments:
            parser.add_argument(*names, **options)


class CommandRegistry:
    def __init__(self):
        self.commands = []

    def add_parser(self, name: str, *, help: str):
        definition = CommandDefinition(name, help)
        self.commands.append(definition)
        return definition


def register_commands(sub) -> None:
    sub.add_parser("app.start", help="Start the Debug app or reuse its connection without showing an existing window")
    sub.add_parser("app.status", help="Read the running Debug app state and focus mode without starting it")
    sub.add_parser("app.ensure", help="Ensure the packaged Debug app is running")
    command = sub.add_parser("app.restart", help="Restart the packaged Debug app")
    command.add_argument("--startup-delay-ms", type=int, default=0, help="Delay UI data initialization by 0 to 30000 ms to inspect the loading overlay")
    command.add_argument("--silent-start", action="store_true", help="Start with the window hidden and wait for the debug port")
    command = sub.add_parser("app.stop", help="Stop the app by command or exact executable path")
    command.add_argument("--output-dir", type=Path, help="Stop only this repository build output, including a previous split-process package")
    sub.add_parser("app.memory", help="Read process and managed memory counters")
    sub.add_parser("app.power-state", help="Read power recovery and platform monitoring state")
    command = sub.add_parser("app.power-event", help="Simulate a suspend or resume notification without changing system power state")
    command.add_argument("event", choices=["suspend", "resume"])
    sub.add_parser("toast.state", help="Read toast visibility and text")
    command = sub.add_parser("window.verify-hover", help="Observe close-button hover through native mouse input on Windows")
    command.add_argument("--assert-cleared", action="store_true", help="Fail if stale hover survives hiding")
    sub.add_parser("window.state", help="Read current window position, size, and display state")
    sub.add_parser("window.appearance.state", help="Read actual transparency, surface opacity, and native composition state")
    command = sub.add_parser("window.appearance.probe", help="Sample acrylic state on composition updates and log per-page results")
    command.add_argument("duration_ms", type=int, nargs="?", default=2000, help="Sampling duration, 200 to 300000 ms")
    sub.add_parser("window.appearance.stop", help="Stop appearance sampling and return its logged summary")
    sub.add_parser("window.minimize", help="Minimize the main window without hiding it to the tray; return window state")
    sub.add_parser("window.maximize", help="Set the main window to maximized without toggling; return window state")
    sub.add_parser("window.restore", help="Restore the main window to normal size without showing a hidden window; return window state")
    command = sub.add_parser("window.move", help="Move the current window to screen coordinates")
    command.add_argument("x", type=int)
    command.add_argument("y", type=int)
    command = sub.add_parser("window.resize", help="Resize the current window; clamped to the window minimum size")
    command.add_argument("width", type=float)
    command.add_argument("height", type=float)
    command = sub.add_parser("app.verify", help="Run the repository post-build automation tests")
    command.add_argument("--shortcuts", action="store_true", help="Verify only global shortcuts and hidden window restoration")
    sub.add_parser("window.close", help="Request the main window to close")
    sub.add_parser("window.show", help="Show the main window using its current focus mode")
    sub.add_parser("screenshot.take", help="Capture a screenshot into build/screenshots/")
    sub.add_parser("clipboard.read", help="Read current window clipboard text")
    command = sub.add_parser("keyboard.press", help="Send an app-level key or shortcut to the focused control")
    command.add_argument("shortcut")
    command = sub.add_parser("hotkey.trigger", help="Simulate a registered global shortcut action")
    command.add_argument("action", choices=["window", "system-proxy", "tun"])
    sub.add_parser("control.catalog", help="Scan XAML under Views and print app-debug commands for each AutomationId")
    command = sub.add_parser("control.list", help="List controls or visible business rows")
    command.add_argument("resource", nargs="?", choices=["nodes", "rules", "connections", "core-logs"])
    command.add_argument("automation_id", nargs="?")
    command.add_argument("--visible", action="store_true")
    command = sub.add_parser("control.get", help="Read control data")
    command.add_argument("resource", choices=["text", "pointer"])
    command.add_argument("automation_id")
    command = sub.add_parser("control.scroll", help="Read or set a control scroll offset")
    command.add_argument("axis", choices=["y"])
    command.add_argument("automation_id")
    command.add_argument("value", nargs="?")
    command = sub.add_parser("control.exists", help="Assert that an AutomationId control exists")
    command.add_argument("automation_id")
    command = sub.add_parser("control.click", help="Click a control by AutomationId")
    command.add_argument("automation_id")
    command = sub.add_parser("control.measure", help="Read a control's window-relative geometry, opacity, classes, and icon kind")
    command.add_argument("automation_id")
    command = sub.add_parser("control.transition", help="Click a control and sample an observed control on animation frames")
    command.add_argument("observe_id")
    command.add_argument("click_id")
    command.add_argument("duration_ms", type=int, nargs="?", default=600)
    command = sub.add_parser("control.input", help="Set TextBox text by AutomationId")
    command.add_argument("automation_id")
    command.add_argument("value")
    command = sub.add_parser("dropdown.list", help="List dropdown data")
    command.add_argument("resource", choices=["items"])
    command.add_argument("automation_id")
    command = sub.add_parser("dropdown.open", help="Open a dropdown by AutomationId")
    command.add_argument("automation_id")
    command = sub.add_parser("dropdown.select", help="Select a ComboBox item by index")
    command.add_argument("automation_id")
    command.add_argument("index", type=int)
    command = sub.add_parser("page.open", help="Open a page")
    command.add_argument("page")
    sub.add_parser("page.state", help="Read the current page loading, overlay, and content visibility state")
    command = sub.add_parser("page.scroll", help="Read or set the current page scroll offset")
    command.add_argument("axis", choices=["y"])
    command.add_argument("value", nargs="?")

    sub.add_parser("render.state", help="Read renderer overlay state, scaling, and transparency")
    sub.add_parser("render.cache", help="Read per-page-host bitmap cache and visibility state")
    command = sub.add_parser("render.cache-mode", help="Read or toggle transition bitmap caching")
    command.add_argument("mode", nargs="?", choices=["on", "off"])
    command = sub.add_parser("render.probe", help="Sample frame intervals into the app log")
    command.add_argument("duration_ms", nargs="?", help="Sampling window in ms (default 2000)")
    command = sub.add_parser("render.overlays", help="Read or set renderer debug overlays")
    command.add_argument(
        "overlays",
        nargs="?",
        help="none | all | comma list of fps,dirty-rects,layout-time,render-time",
    )

    sub.add_parser("home.state", help="Read Home page business state")
    command = sub.add_parser("home.refresh", help="Refresh Home state")
    command.add_argument("target", choices=["runtime", "network"])
    command = sub.add_parser("home.set", help="Set Home state")
    command.add_argument("target", choices=["outbound", "tun"])
    command.add_argument("value")
    command = sub.add_parser("home.select", help="Select a Home area")
    command.add_argument("target", choices=["takeover"])
    command.add_argument("value", choices=["proxy", "tun"])
    command = sub.add_parser("home.copy", help="Copy Home data")
    command.add_argument("target", choices=["terminal-proxy"])
    command.add_argument("value", choices=["powershell", "cmd", "bash"])
    command = sub.add_parser("home.toggle", help="Toggle Home state")
    command.add_argument("target", choices=["system-proxy"])
    command = sub.add_parser("home.reset", help="Reset Home state")
    command.add_argument("target", choices=["traffic"])
    command = sub.add_parser("home.restart", help="Restart a Home runtime component")
    command.add_argument("target", choices=["core"])
    command = sub.add_parser("home.update", help="Update a Home runtime component")
    command.add_argument("target", choices=["core"])

    sub.add_parser("service.state", help="Read service mode state")
    sub.add_parser("service.install", help="Install service mode")
    sub.add_parser("service.update", help="Update service mode")
    sub.add_parser("service.repair", help="Repair service mode bound to another app directory")
    sub.add_parser("service.uninstall", help="Uninstall service mode")

    command = sub.add_parser("proxies.list", help="List proxy data")
    command.add_argument("resource", choices=["groups", "nodes"])
    command.add_argument("group", nargs="?")
    command = sub.add_parser("proxies.get", help="Read proxy data")
    command.add_argument("resource", choices=["delay"])
    command.add_argument("name")
    command = sub.add_parser("proxies.select", help="Select proxy data")
    command.add_argument("resource", choices=["group", "node"])
    command.add_argument("name")
    command = sub.add_parser("proxies.test", help="Run proxy delay tests")
    command.add_argument("target", choices=["node", "group", "current-group", "all"])
    command.add_argument("name", nargs="?")
    sub.add_parser("proxies.refresh", help="Refresh proxy page data")
    command = sub.add_parser("proxies.cancel", help="Cancel proxy work")
    command.add_argument("target", choices=["delay"])
    sub.add_parser("proxies.state", help="Read proxy page business state")
    command = sub.add_parser("proxies.set", help="Set proxy page state")
    command.add_argument("target", choices=["layout"])
    command.add_argument("value", choices=["horizontal", "vertical"])
    command = sub.add_parser("proxies.toggle", help="Toggle proxy page state")
    command.add_argument("target", choices=["layout"])
    command = sub.add_parser("proxies.expand", help="Expand proxy page data")
    command.add_argument("target", choices=["group"])
    command.add_argument("name")
    command = sub.add_parser("proxies.collapse", help="Collapse proxy page data")
    command.add_argument("target", choices=["group"])

    sub.add_parser("connections.refresh", help="Refresh the connection list")
    command = sub.add_parser("connections.toggle", help="Toggle connection state")
    command.add_argument("target", choices=["pause"])
    command = sub.add_parser("connections.close", help="Close one connection or all connections")
    command.add_argument("target")
    command = sub.add_parser("connections.filter", help="Switch the connection filter")
    command.add_argument("type", choices=["all", "direct", "proxy"])
    command = sub.add_parser("connections.get", help="Read connection data")
    command.add_argument("resource", choices=["detail"])
    command.add_argument("connection_id")
    sub.add_parser("connections.state", help="Read connections page business state")

    command = sub.add_parser("core-logs.toggle", help="Toggle core log state")
    command.add_argument("target", choices=["pause"])
    sub.add_parser("core-logs.clear", help="Clear core logs")
    command = sub.add_parser("core-logs.filter", help="Switch the core log filter")
    command.add_argument("level", choices=["all", "debug", "info", "warning", "error"])
    sub.add_parser("core-logs.state", help="Read core logs page business state")
    sub.add_parser("core.state", help="Read hub core state")

    sub.add_parser("rules.refresh", help="Refresh the rule list")
    command = sub.add_parser("rules.filter", help="Switch the rule type filter")
    command.add_argument("bucket", choices=["all", "domain", "ip", "rule-set", "other"])
    command = sub.add_parser("rules.search", help="Set the rule search keyword")
    command.add_argument("keyword")
    command = sub.add_parser("rules.list", help="List currently filtered rules or rules by source")
    command.add_argument("source", nargs="?", choices=["builtin", "custom"])
    sub.add_parser("rules.state", help="Read rules page business state")
    command = sub.add_parser("rules.move", help="Move a visible rule")
    command.add_argument("direction", choices=["up", "down"])
    command.add_argument("rule_id")
    command = sub.add_parser("rules.add", help="Add a custom rule")
    command.add_argument("source", choices=["custom"])
    command.add_argument("type")
    command.add_argument("payload")
    command.add_argument("outbound")
    command.add_argument("--no-resolve", action="store_true")
    command = sub.add_parser("rules.edit", help="Edit a custom rule")
    command.add_argument("source", choices=["custom"])
    command.add_argument("rule_id")
    command.add_argument("type")
    command.add_argument("payload")
    command.add_argument("outbound")
    command.add_argument("--no-resolve", action="store_true")
    command = sub.add_parser("rules.remove", help="Remove a custom rule")
    command.add_argument("source", choices=["custom"])
    command.add_argument("rule_id")
    command = sub.add_parser("rules.toggle", help="Toggle a built-in or custom rule")
    command.add_argument("source", choices=["builtin", "custom"])
    command.add_argument("rule_id")
    sub.add_parser("rules.reset-order", help="Reset custom rule order")

    command = sub.add_parser("subscriptions.add", help="Add a subscription")
    command.add_argument("source", choices=["remote", "local"])
    command.add_argument("name")
    command.add_argument("location")
    command.add_argument("--ua")
    command.add_argument("--age-key")
    command.add_argument("--auto", choices=["disabled", "startup", "interval"])
    command.add_argument("--interval", type=int)
    command.add_argument("--proxy", choices=["direct", "system", "core"])
    command = sub.add_parser("subscriptions.paste", help="Paste subscription dialog data")
    command.add_argument("target", choices=["url"])
    command = sub.add_parser("subscriptions.select", help="Select a subscription")
    command.add_argument("subscription_id")
    command = sub.add_parser("subscriptions.set", help="Set subscription state")
    command.add_argument("target", choices=["overrides", "update-delay"])
    command.add_argument("values", nargs="*")
    command = sub.add_parser("subscriptions.update", help="Update one subscription or all subscriptions")
    command.add_argument("target")
    sub.add_parser("subscriptions.list", help="List subscriptions")
    command = sub.add_parser("subscriptions.state", help="Read subscription state")
    command.add_argument("scope", nargs="?", choices=["store", "selection"])
    command = sub.add_parser("subscriptions.delete", help="Delete a subscription")
    command.add_argument("subscription_id")
    command = sub.add_parser("subscriptions.move", help="Move a subscription")
    command.add_argument("direction", choices=["up", "down"])
    command.add_argument("subscription_id")
    command = sub.add_parser("subscriptions.copy", help="Copy subscription data")
    command.add_argument("target", choices=["link"])
    command.add_argument("subscription_id")
    command = sub.add_parser("subscriptions.show", help="Show subscription data")
    command.add_argument("target", choices=["qr"])
    command.add_argument("subscription_id")
    command = sub.add_parser("subscriptions.chain-proxy.open", help="Open subscription chain proxy management")
    command.add_argument("subscription_id")
    command = sub.add_parser("subscriptions.chain-proxy.start", help="Start a chain proxy action")
    command.add_argument("target", choices=["add"])
    command = sub.add_parser("subscriptions.chain-proxy.set", help="Set chain proxy draft state")
    command.add_argument("target", choices=["name", "group"])
    command.add_argument("value")
    command = sub.add_parser("subscriptions.chain-proxy.toggle", help="Toggle chain proxy state")
    command.add_argument("target", choices=["hop", "builtin", "custom"])
    command.add_argument("value")
    command.add_argument("name", nargs="?")
    command = sub.add_parser("subscriptions.chain-proxy.save", help="Save chain proxy state")
    command.add_argument("target", nargs="?", choices=["draft"])
    command = sub.add_parser("subscriptions.chain-proxy.remove", help="Remove chain proxy data")
    command.add_argument("target", choices=["custom"])
    command.add_argument("custom_id")
    command = sub.add_parser("subscriptions.chain-proxy.edit", help="Edit chain proxy data")
    command.add_argument("target", choices=["custom"])
    command.add_argument("custom_id")
    command = sub.add_parser("subscriptions.chain-proxy.state", help="Read chain proxy state")
    command.add_argument("scope", nargs="?", choices=["draft"])
    command = sub.add_parser("subscriptions.chain-proxy.move", help="Move a chain proxy hop")
    command.add_argument("direction", choices=["up", "down"])
    command.add_argument("hop_name")
    command = sub.add_parser("subscriptions.override-selector.move", help="Move an override selector item")
    command.add_argument("direction", choices=["up", "down"])
    command.add_argument("override_id")
    command = sub.add_parser("subscriptions.open", help="Open subscription data")
    command.add_argument("target", choices=["external-editor"])
    command.add_argument("subscription_id")
    command = sub.add_parser("subscriptions.get", help="Read subscription data")
    command.add_argument("target", choices=["runtime-config"])
    command.add_argument("subscription_id")
    command = sub.add_parser("subscriptions.save", help="Save subscription data")
    command.add_argument("target", choices=["file"])
    command.add_argument("subscription_id")
    command.add_argument("content")
    command = sub.add_parser("subscriptions.edit", help="Edit subscription data")
    command.add_argument("target", choices=["metadata", "file"])
    command.add_argument("subscription_id")
    command.add_argument("--name")
    command.add_argument("--url")
    command.add_argument("--ua")
    command.add_argument("--age-key")
    command.add_argument("--delay", type=int)
    command.add_argument("--auto", choices=["disabled", "startup", "interval"])
    command.add_argument("--interval", type=int)
    command.add_argument("--proxy", choices=["direct", "system", "core"])
    command = sub.add_parser("subscriptions.provider.list", help="List subscription providers")
    command.add_argument("subscription_id")
    sub.add_parser("subscriptions.provider.rows", help="Read current provider dialog rows")
    command = sub.add_parser("subscriptions.provider.sync", help="Sync one provider or all providers")
    command.add_argument("values", nargs="+")
    command = sub.add_parser("subscriptions.provider.upload", help="Upload File Provider content")
    command.add_argument("subscription_id")
    command.add_argument("provider_name")
    command.add_argument("path")
    command = sub.add_parser("subscriptions.trigger", help="Trigger subscription debug work")
    command.add_argument("target", choices=["auto-delay"])

    command = sub.add_parser("overrides.add", help="Add an override")
    command.add_argument("source", choices=["remote", "local"])
    command.add_argument("name")
    command.add_argument("location")
    command.add_argument("--format", choices=["yaml", "javascript", "js"])
    command.add_argument("--proxy", choices=["direct", "system", "core"])
    command = sub.add_parser("overrides.paste", help="Paste override dialog data")
    command.add_argument("target", choices=["url"])
    command = sub.add_parser("overrides.create", help="Create an override")
    command.add_argument("target", choices=["blank", "inline"])
    command.add_argument("name")
    command.add_argument("content", nargs="?")
    command.add_argument("--format", choices=["yaml", "javascript", "js"])
    command = sub.add_parser("overrides.select", help="Select an override")
    command.add_argument("override_id")
    command = sub.add_parser("overrides.update", help="Update one override or all overrides")
    command.add_argument("target")
    command = sub.add_parser("overrides.delete", help="Delete an override")
    command.add_argument("override_id")
    command = sub.add_parser("overrides.move", help="Move an override")
    command.add_argument("direction", choices=["up", "down"])
    command.add_argument("override_id")
    command = sub.add_parser("overrides.edit", help="Edit override data")
    command.add_argument("target", choices=["metadata", "file"])
    command.add_argument("override_id")
    command = sub.add_parser("overrides.save", help="Save override data")
    command.add_argument("target", choices=["file"])
    command.add_argument("override_id")
    command.add_argument("content")
    command = sub.add_parser("overrides.open", help="Open override data")
    command.add_argument("target", choices=["external-editor"])
    command.add_argument("override_id")
    sub.add_parser("overrides.list", help="List overrides")
    sub.add_parser("overrides.state", help="Read override page business state")

    sub.add_parser("settings.state", help="Read settings page app preference state")
    command = sub.add_parser("settings.language.set", help="Set language")
    command.add_argument("code")
    sub.add_parser("settings.language.state", help="Read language setting state")
    command = sub.add_parser("settings.theme.set", help="Set theme")
    command.add_argument("theme")
    sub.add_parser("settings.theme.state", help="Read theme setting state")
    command = sub.add_parser("settings.accent.set", help="Set accent state")
    command.add_argument("target", choices=["mode", "color"])
    command.add_argument("value")
    command = sub.add_parser("settings.window-effect.set", help="Set window effect")
    command.add_argument("effect")
    command = sub.add_parser("settings.app-behavior.list", help="List app behavior data")
    command.add_argument("target", choices=["keys"])
    command = sub.add_parser("settings.app-behavior.set", help="Set an app behavior item")
    command.add_argument("key")
    command.add_argument("value")
    sub.add_parser("settings.app-behavior.state", help="Read app behavior setting state")
    command = sub.add_parser("settings.update.set", help="Set app update state")
    command.add_argument("target", choices=["auto-check", "interval", "channel"])
    command.add_argument("value")
    sub.add_parser("settings.update.check", help="Check for app updates manually")
    command = sub.add_parser("settings.update.ignore", help="Ignore app update data")
    command.add_argument("target", choices=["latest"])
    sub.add_parser("settings.update.state", help="Read app update setting state")
    command = sub.add_parser("settings.data-management.backup", help="Create a data backup")
    command.add_argument("path")
    command = sub.add_parser("settings.data-management.restore", help="Restore a backup")
    command.add_argument("path")
    command.add_argument("mode", choices=["overwrite", "merge"])
    sub.add_parser("settings.data-management.state", help="Read data management setting state")
    command = sub.add_parser("settings.data-management.webdav.list", help="List WebDAV data")
    command.add_argument("target", nargs="?", choices=["keys"])
    command = sub.add_parser("settings.data-management.webdav.set", help="Set a WebDAV item")
    command.add_argument("key")
    command.add_argument("value")
    sub.add_parser("settings.data-management.webdav.test", help="Test the configured WebDAV endpoint")
    sub.add_parser("settings.data-management.webdav.backup", help="Create and upload a WebDAV backup")
    command = sub.add_parser("settings.data-management.webdav.open", help="Open WebDAV data")
    command.add_argument("target", choices=["dialog"])
    command = sub.add_parser("settings.data-management.webdav.restore", help="Restore WebDAV data")
    command.add_argument("target", choices=["backup", "overwrite", "merge"])
    command.add_argument("value", nargs="?")
    command = sub.add_parser("settings.data-management.webdav.delete", help="Delete WebDAV data")
    command.add_argument("target", choices=["backup"])
    command.add_argument("file_name")
    sub.add_parser("settings.data-management.webdav.state", help="Read WebDAV data backup state")
    sub.add_parser("settings.app-log.refresh", help="Refresh app logs")
    command = sub.add_parser("settings.app-log.export", help="Export app logs")
    command.add_argument("path")
    sub.add_parser("settings.app-log.state", help="Read app log page state")
    command = sub.add_parser("settings.system-integration.list", help="List system integration data")
    command.add_argument("target", choices=["keys"])
    command = sub.add_parser("settings.system-integration.set", help="Set a system integration item")
    command.add_argument("key")
    command.add_argument("value")
    command = sub.add_parser("settings.system-integration.refresh", help="Refresh system integration data")
    command.add_argument("target", choices=["proxy-host"])
    sub.add_parser("settings.system-integration.uwp.list", help="Open the UWP dialog and list all rows")
    command = sub.add_parser("settings.system-integration.uwp.search", help="Filter UWP rows")
    command.add_argument("keyword")
    command = sub.add_parser("settings.system-integration.uwp.select", help="Select UWP rows")
    command.add_argument("target", choices=["all"])
    sub.add_parser("settings.system-integration.uwp.invert", help="Invert UWP row selection")
    command = sub.add_parser("settings.system-integration.uwp.set", help="Set UWP checked state")
    command.add_argument("package_family_name")
    command.add_argument("value", choices=BOOLEAN_CHOICES)
    sub.add_parser("settings.system-integration.uwp.save", help="Save UWP loopback selections")
    sub.add_parser("settings.system-integration.uwp.cancel", help="Close the UWP dialog without saving")
    sub.add_parser("settings.system-integration.state", help="Read system integration setting state")
    command = sub.add_parser("clash.list", help="List Clash data")
    command.add_argument("target", choices=["keys"])
    command = sub.add_parser("clash.get", help="Read a Clash setting")
    command.add_argument("key")
    command = sub.add_parser("clash.set", help="Change a Clash setting")
    command.add_argument("key")
    command.add_argument("value")
    sub.add_parser("clash.state", help="Read Clash setting refresh state")

def build_server_command(args: argparse.Namespace) -> str:
    match args.cmd:
        case "app.power-event":
            return f"app.power-event {args.event}"
        case "window.appearance.probe":
            return f"window.appearance.probe {args.duration_ms}"
        case "window.move":
            return f"window.move {args.x} {args.y}"
        case "window.resize":
            return f"window.resize {args.width} {args.height}"
        case "keyboard.press":
            return f"keyboard.press {args.shortcut}"
        case "hotkey.trigger":
            return f"hotkey.trigger {args.action}"
        case "control.list":
            if args.resource:
                if not args.automation_id or args.visible:
                    raise RuntimeError("control.list <resource> requires an AutomationId and does not accept --visible")
                return f"control.list {args.resource} {args.automation_id}"
            if args.automation_id:
                raise RuntimeError("control.list only accepts an AutomationId after a resource")
            return "control.list --visible" if args.visible else "control.list"
        case "control.get":
            return f"control.get {args.resource} {args.automation_id}"
        case "control.scroll":
            value = f" {args.value}" if args.value is not None else ""
            return f"control.scroll {args.axis} {args.automation_id}{value}"
        case "control.exists" | "control.click" | "control.measure":
            return f"{args.cmd} {args.automation_id}"
        case "control.transition":
            return f"control.transition {args.observe_id} {args.click_id} {args.duration_ms}"
        case "control.input":
            value = "__EMPTY__" if args.value.strip() == "" else args.value
            return f"control.input {args.automation_id} {value}"
        case "dropdown.list":
            return f"dropdown.list {args.resource} {args.automation_id}"
        case "dropdown.open":
            return f"dropdown.open {args.automation_id}"
        case "dropdown.select":
            return f"dropdown.select {args.automation_id} {args.index}"
        case "page.open":
            return f"page.open {args.page}"
        case "page.scroll":
            value = f" {args.value}" if args.value is not None else ""
            return f"page.scroll {args.axis}{value}"
        case "render.probe":
            return f"render.probe {args.duration_ms}" if args.duration_ms else "render.probe"
        case "render.cache-mode":
            return f"render.cache-mode {args.mode}" if args.mode else "render.cache-mode"
        case "render.overlays":
            return f"render.overlays {args.overlays}" if args.overlays else "render.overlays"
        case "home.refresh" | "home.toggle" | "home.reset" | "home.restart" | "home.update":
            return f"{args.cmd} {args.target}"
        case "home.set" | "home.select" | "home.copy":
            return f"{args.cmd} {args.target} {quote_arg(args.value)}"
        case "proxies.list":
            group = f" {quote_arg(args.group)}" if args.group else ""
            if args.resource == "groups" and group:
                raise RuntimeError("proxies.list groups does not accept a group name")
            return f"proxies.list {args.resource}{group}"
        case "proxies.get" | "proxies.select":
            return f"{args.cmd} {args.resource} {quote_arg(args.name)}"
        case "proxies.test":
            if args.target in {"node", "group"}:
                if not args.name:
                    raise RuntimeError(f"proxies.test {args.target} requires a name")
                return f"proxies.test {args.target} {quote_arg(args.name)}"
            if args.name:
                raise RuntimeError(f"proxies.test {args.target} does not accept a name")
            return f"proxies.test {args.target}"
        case "proxies.cancel" | "proxies.toggle" | "proxies.collapse":
            return f"{args.cmd} {args.target}"
        case "proxies.set":
            return f"proxies.set {args.target} {args.value}"
        case "proxies.expand":
            return f"proxies.expand {args.target} {quote_arg(args.name)}"
        case "connections.toggle":
            return f"connections.toggle {args.target}"
        case "connections.close":
            return f"connections.close {quote_arg(args.target)}"
        case "connections.filter":
            return f"connections.filter {args.type}"
        case "connections.get":
            return f"connections.get {args.resource} {quote_arg(args.connection_id)}"
        case "core-logs.toggle":
            return f"core-logs.toggle {args.target}"
        case "core-logs.filter":
            return f"core-logs.filter {args.level}"
        case "rules.filter":
            return f"rules.filter {args.bucket}"
        case "rules.search":
            return f"rules.search {quote_arg(args.keyword)}"
        case "rules.list":
            return f"rules.list {args.source}" if args.source else "rules.list"
        case "rules.move":
            return f"rules.move {args.direction} {quote_arg(args.rule_id)}"
        case "rules.add":
            suffix = " --no-resolve" if args.no_resolve else ""
            return f"rules.add {args.source} {args.type} {quote_arg(args.payload)} {quote_arg(args.outbound)}{suffix}"
        case "rules.edit":
            suffix = " --no-resolve" if args.no_resolve else ""
            return f"rules.edit {args.source} {quote_arg(args.rule_id)} {args.type} {quote_arg(args.payload)} {quote_arg(args.outbound)}{suffix}"
        case "rules.remove" | "rules.toggle":
            return f"{args.cmd} {args.source} {quote_arg(args.rule_id)}"
        case "subscriptions.add":
            flags = [args.source, quote_arg(args.name), quote_arg(args.location)]
            if args.source == "local" and any([args.ua, args.age_key, args.auto, args.interval, args.proxy]):
                raise RuntimeError("subscriptions.add local does not accept remote subscription options")
            if args.ua:
                flags.extend(["--ua", quote_arg(args.ua)])
            if args.age_key:
                flags.extend(["--age-key", quote_arg(args.age_key)])
            if args.auto:
                flags.extend(["--auto", args.auto])
            if args.interval is not None:
                flags.extend(["--interval", str(args.interval)])
            if args.proxy:
                flags.extend(["--proxy", args.proxy])
            return "subscriptions.add " + " ".join(flags)
        case "subscriptions.paste":
            return f"subscriptions.paste {args.target}"
        case "subscriptions.select" | "subscriptions.delete":
            return f"{args.cmd} {args.subscription_id}"
        case "subscriptions.set":
            if args.target == "overrides":
                if not args.values:
                    raise RuntimeError("subscriptions.set overrides requires a subscription ID")
                values = " ".join(args.values) if len(args.values) > 1 else f"{args.values[0]} __EMPTY__"
                return f"subscriptions.set overrides {values}"
            if len(args.values) != 1:
                raise RuntimeError("subscriptions.set update-delay requires milliseconds")
            return f"subscriptions.set update-delay {args.values[0]}"
        case "subscriptions.update":
            return f"subscriptions.update {args.target}"
        case "subscriptions.state":
            return f"subscriptions.state {args.scope}" if args.scope else "subscriptions.state"
        case "subscriptions.move":
            return f"subscriptions.move {args.direction} {args.subscription_id}"
        case "subscriptions.copy" | "subscriptions.show":
            return f"{args.cmd} {args.target} {args.subscription_id}"
        case "subscriptions.chain-proxy.open":
            return f"subscriptions.chain-proxy.open {args.subscription_id}"
        case "subscriptions.chain-proxy.start":
            return f"subscriptions.chain-proxy.start {args.target}"
        case "subscriptions.chain-proxy.set":
            return f"subscriptions.chain-proxy.set {args.target} {quote_arg(args.value)}"
        case "subscriptions.chain-proxy.toggle":
            if args.target == "hop":
                if args.value not in {"proxy", "group"} or not args.name:
                    raise RuntimeError("subscriptions.chain-proxy.toggle hop requires <proxy|group> <name>")
                return f"subscriptions.chain-proxy.toggle hop {args.value} {quote_arg(args.name)}"
            if args.name:
                raise RuntimeError(f"subscriptions.chain-proxy.toggle {args.target} accepts one value")
            return f"subscriptions.chain-proxy.toggle {args.target} {quote_arg(args.value)}"
        case "subscriptions.chain-proxy.save":
            return "subscriptions.chain-proxy.save draft" if args.target else "subscriptions.chain-proxy.save"
        case "subscriptions.chain-proxy.remove" | "subscriptions.chain-proxy.edit":
            return f"{args.cmd} {args.target} {quote_arg(args.custom_id)}"
        case "subscriptions.chain-proxy.state":
            return "subscriptions.chain-proxy.state draft" if args.scope else "subscriptions.chain-proxy.state"
        case "subscriptions.chain-proxy.move":
            return f"subscriptions.chain-proxy.move {args.direction} {quote_arg(args.hop_name)}"
        case "subscriptions.override-selector.move":
            return f"subscriptions.override-selector.move {args.direction} {quote_arg(args.override_id)}"
        case "subscriptions.open" | "subscriptions.get":
            return f"{args.cmd} {args.target} {args.subscription_id}"
        case "subscriptions.save":
            return f"subscriptions.save {args.target} {args.subscription_id} {quote_arg(args.content)}"
        case "subscriptions.edit":
            if args.target == "file":
                if any([args.name, args.url, args.ua, args.age_key, args.delay, args.auto, args.interval, args.proxy]):
                    raise RuntimeError("subscriptions.edit file does not accept metadata options")
                return f"subscriptions.edit file {args.subscription_id}"
            flags = ["metadata", args.subscription_id]
            for option, value in [
                ("--name", args.name),
                ("--url", args.url),
                ("--ua", args.ua),
                ("--age-key", args.age_key),
                ("--delay", args.delay),
                ("--auto", args.auto),
                ("--interval", args.interval),
                ("--proxy", args.proxy),
            ]:
                if value is not None:
                    flags.extend([option, quote_arg(str(value))])
            return "subscriptions.edit " + " ".join(flags)
        case "subscriptions.provider.list":
            return f"subscriptions.provider.list {args.subscription_id}"
        case "subscriptions.provider.sync":
            if len(args.values) == 2 and args.values[0] == "all":
                return f"subscriptions.provider.sync all {args.values[1]}"
            if len(args.values) == 2:
                return f"subscriptions.provider.sync {args.values[0]} {quote_arg(args.values[1])}"
            raise RuntimeError("subscriptions.provider.sync requires <subscription_id> <provider_name> or all <subscription_id>")
        case "subscriptions.provider.upload":
            return f"subscriptions.provider.upload {args.subscription_id} {quote_arg(args.provider_name)} {quote_arg(args.path)}"
        case "subscriptions.trigger":
            return f"subscriptions.trigger {args.target}"
        case "overrides.add":
            flags = [args.source, quote_arg(args.name), quote_arg(args.location)]
            if args.source == "local" and args.proxy:
                raise RuntimeError("overrides.add local does not accept --proxy")
            if args.format:
                flags.extend(["--format", args.format])
            if args.proxy:
                flags.extend(["--proxy", args.proxy])
            return "overrides.add " + " ".join(flags)
        case "overrides.paste":
            return f"overrides.paste {args.target}"
        case "overrides.create":
            if args.target == "inline" and args.content is None:
                raise RuntimeError("overrides.create inline requires content")
            if args.target == "blank" and args.content is not None:
                raise RuntimeError("overrides.create blank does not accept content")
            flags = [args.target, quote_arg(args.name)]
            if args.content is not None:
                flags.append(quote_arg(args.content))
            if args.format:
                flags.extend(["--format", args.format])
            return "overrides.create " + " ".join(flags)
        case "overrides.select" | "overrides.delete":
            return f"{args.cmd} {args.override_id}"
        case "overrides.update":
            return f"overrides.update {args.target}"
        case "overrides.move":
            return f"overrides.move {args.direction} {args.override_id}"
        case "overrides.edit" | "overrides.open":
            return f"{args.cmd} {args.target} {args.override_id}"
        case "overrides.save":
            return f"overrides.save {args.target} {args.override_id} {quote_arg(args.content)}"
        case "settings.language.set":
            return f"settings.language.set {args.code}"
        case "settings.theme.set":
            return f"settings.theme.set {args.theme}"
        case "settings.accent.set":
            return f"settings.accent.set {args.target} {quote_arg(args.value)}"
        case "settings.window-effect.set":
            return f"settings.window-effect.set {args.effect}"
        case "settings.app-behavior.list" | "settings.system-integration.list":
            return f"{args.cmd} {args.target}"
        case "settings.app-behavior.set" | "settings.system-integration.set":
            return f"{args.cmd} {args.key} {quote_arg(args.value)}"
        case "settings.update.set":
            return f"settings.update.set {args.target} {args.value}"
        case "settings.update.ignore":
            return f"settings.update.ignore {args.target}"
        case "settings.data-management.backup":
            return f"settings.data-management.backup {quote_arg(args.path)}"
        case "settings.data-management.restore":
            return f"settings.data-management.restore {quote_arg(args.path)} {args.mode}"
        case "settings.data-management.webdav.list":
            return "settings.data-management.webdav.list keys" if args.target else "settings.data-management.webdav.list"
        case "settings.data-management.webdav.set":
            return f"settings.data-management.webdav.set {args.key} {quote_arg(args.value)}"
        case "settings.data-management.webdav.open":
            return f"settings.data-management.webdav.open {args.target}"
        case "settings.data-management.webdav.restore":
            if args.target == "backup":
                if not args.value:
                    raise RuntimeError("settings.data-management.webdav.restore backup requires a file name")
                return f"settings.data-management.webdav.restore backup {quote_arg(args.value)}"
            if args.value:
                raise RuntimeError(f"settings.data-management.webdav.restore {args.target} does not accept a value")
            return f"settings.data-management.webdav.restore {args.target}"
        case "settings.data-management.webdav.delete":
            return f"settings.data-management.webdav.delete {args.target} {quote_arg(args.file_name)}"
        case "settings.app-log.export":
            return f"settings.app-log.export {quote_arg(args.path)}"
        case "settings.system-integration.refresh" | "settings.system-integration.uwp.select":
            return f"{args.cmd} {args.target}"
        case "settings.system-integration.uwp.search":
            return f"settings.system-integration.uwp.search {quote_arg(args.keyword)}"
        case "settings.system-integration.uwp.set":
            return f"settings.system-integration.uwp.set {quote_arg(args.package_family_name)} {args.value}"
        case "clash.list":
            return f"clash.list {args.target}"
        case "clash.get":
            return f"clash.get {args.key}"
        case "clash.set":
            return f"clash.set {args.key} {args.value}"
        case command if command in {
            "app.memory", "app.power-state", "toast.state", "clipboard.read",
            "window.state", "window.close", "window.show",
            "window.appearance.state", "window.appearance.stop",
            "window.minimize", "window.maximize", "window.restore",
            "home.state", "page.state",
            "service.state", "service.install", "service.update", "service.repair", "service.uninstall",
            "proxies.refresh", "proxies.state", "connections.refresh", "connections.state",
            "core-logs.clear", "core-logs.state", "core.state", "rules.refresh", "rules.reset-order", "rules.list", "rules.state",
            "subscriptions.list", "subscriptions.provider.rows", "overrides.list", "overrides.state",
            "settings.state", "settings.language.state", "settings.theme.state", "settings.app-behavior.state",
            "settings.update.check", "settings.update.state", "settings.data-management.state",
            "settings.data-management.webdav.test", "settings.data-management.webdav.backup",
            "settings.data-management.webdav.state", "settings.app-log.refresh", "settings.app-log.state",
            "settings.system-integration.uwp.list", "settings.system-integration.uwp.invert",
            "settings.system-integration.uwp.save", "settings.system-integration.uwp.cancel",
            "settings.system-integration.state", "clash.state",
            "render.state", "render.cache",
        }:
            return command
        case _:
            raise RuntimeError(f"Unknown command: {args.cmd}")

def quote_arg(value: str) -> str:
    if value == "":
        return "''"
    if not any(character.isspace() or character in "'\"\\" for character in value):
        return value
    return "'" + value.replace("\\", "\\\\").replace("'", "\\'") + "'"

registry = CommandRegistry()
register_commands(registry)



# handler 使用 app_debug.<模块>:<函数>，执行函数接收 Namespace 并返回可 JSON 编码的结果。
# 实际失败抛出 CommandFailure、CalledProcessError 或对应的文件/业务异常。

# 启动参数声明 supports_no_focus；运行中切换声明 supports_focus_mode。
# 新进程调用 focus_launch_arguments，已有实例省略 focus_mode 时沿用模式。

# 子工具调用显式绑定当前 sys.stdout/sys.stderr，诊断始终写入 stderr。
def validate_arguments(args: argparse.Namespace) -> None:
    if args.cmd == "app.restart" and not 0 <= args.startup_delay_ms <= 30000:
        raise ValueError("startup-delay-ms must be between 0 and 30000")
    if args.cmd not in {"app.start", "app.ensure", "app.status", "app.restart", "app.stop", "app.verify", "window.verify-hover", "control.catalog", "screenshot.take"}:
        build_server_command(args)


EXAMPLES = {
    "app.start": "app.start --no-focus",
    "app.ensure": "app.ensure --no-focus",
    "app.restart": "app.restart --no-focus",
    "app.status": "app.status",
    "app.stop": "app.stop",
    "screenshot.take": "screenshot.take",
    "page.open": "page.open settings/theme",
    "control.measure": "control.measure Sidebar.Root",
    "control.transition": "control.transition Sidebar.Root Settings.NarrowSidebarSwitch 750",
}

COMMANDS: tuple[Command, ...] = tuple(
    Command(
        definition.name, definition.summary, "app_debug.client:execute_command",
        configure=definition.configure,
        examples=("python .codex/skills/app-debug/scripts/run.py " + EXAMPLES[definition.name],) if definition.name in EXAMPLES else (),
        supports_no_focus=definition.name in NO_FOCUS_COMMANDS,
        supports_focus_mode=definition.name in FOCUS_MODE_COMMANDS,
    )
    for definition in registry.commands
)
