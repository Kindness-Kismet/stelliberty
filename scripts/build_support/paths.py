from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DESKTOP_PROJECT = ROOT / "src" / "Stelliberty.Desktop" / "Stelliberty.Desktop.csproj"
TRAY_PROJECT = ROOT / "src" / "Stelliberty.Tray" / "Stelliberty.Tray.csproj"
BUILD_PROPS = ROOT / "Directory.Build.props"
RUST_WORKSPACE = ROOT / "Cargo.toml"
BUILD_DIR = ROOT / "build"
PACKAGES_DIR = BUILD_DIR / "packages"
PRE_ASSETS_DIR = BUILD_DIR / "pre_assets"
TOOLS_DIR = BUILD_DIR / "tools"
WINDOWS_INSTALLER_TEMPLATE = ROOT / "scripts" / "installer" / "windows" / "setup.iss.in"
LINUX_INSTALLER_TEMPLATE_DIR = ROOT / "scripts" / "installer" / "linux"
MACOS_INSTALLER_TEMPLATE_DIR = ROOT / "scripts" / "installer" / "macos"

DATA_DIRECTORY = Path("data")
DEPS_DIRECTORY = DATA_DIRECTORY / "deps"
CORE_DIRECTORY = DATA_DIRECTORY / "core"
SERVICE_DIRECTORY = DATA_DIRECTORY / "service"
SERVICE_UPDATE_DIRECTORY = SERVICE_DIRECTORY / "update"
