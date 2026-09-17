import os
import shutil
import subprocess
import tempfile
from pathlib import Path

from build_support.commands import run
from build_support.fonts import ensure_app_fonts
from build_support.installer import pack_installers
from build_support.layout import organize_dependency_directory, output_name, service_binary_name, zip_output
from build_support.models import AppMetadata, BuildRequest, PlatformTarget
from build_support.paths import BUILD_DIR, CORE_DIRECTORY, DESKTOP_PROJECT, PRE_ASSETS_DIR, ROOT, RUST_WORKSPACE, SERVICE_UPDATE_DIRECTORY, TRAY_PROJECT
from build_support.processes import close_running_output_app
from build_support.timer import timed_step

CORE_ASSET_NAMES = {"asn.mmdb", "country.mmdb", "geoip.dat", "geoip.metadb", "geosite.dat"}

def build(request: BuildRequest) -> None:
    reset_build_servers()

    with timed_step("Generate C# bindings"):
        generate_bindings()

    with timed_step("Prepare font assets"):
        for path in ensure_app_fonts():
            print(f"  Font {path.name}", flush=True)

    for platform_name, target in request.platforms:
        for configuration in request.configurations:
            build_output(request.metadata, platform_name, configuration, target, request.pack_format, request.clean)

def reset_build_servers() -> None:
    subprocess.run(
        ["dotnet", "build-server", "shutdown"],
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        check=False,
    )

def build_output(metadata: AppMetadata, platform_name: str, configuration: str, target: PlatformTarget, pack_format: str | None, clean: bool) -> None:
    output_dir = BUILD_DIR / output_name(metadata, platform_name, configuration)
    build_label = f"{platform_name} {display_configuration(configuration)}"

    with timed_step(f"Check running output app {build_label}"):
        close_running_output_app(metadata, output_dir)

    if clean:
        with timed_step(f"Clean output directory {build_label}"):
            reset_output_directory(output_dir)

    with timed_step(f"Build Rust {build_label}"):
        build_rust(metadata, configuration, target)

    with timed_step(f"Build .NET {build_label}"):
        publish_dotnet(metadata, configuration, target, output_dir)

    copy_native_library(configuration, target, output_dir)
    copy_service_binary(metadata, configuration, target, output_dir)

    with timed_step(f"Copy resource files {build_label}"):
        copy_core_assets(platform_name, output_dir)

    with timed_step(f"Organize dependency directory {build_label}"):
        organize_dependency_directory(output_dir, metadata, configuration)

    if pack_format in ("zip", "all"):
        with timed_step(f"Package zip {build_label}"):
            archive_path = zip_output(output_dir)
            print(f"  Archive {archive_path}", flush=True)

    if pack_format in ("installer", "all"):
        with timed_step(f"Package installer {build_label}"):
            for installer_path in pack_installers(metadata, platform_name, configuration, target, output_dir):
                print(f"  Artifact {installer_path}", flush=True)

    print(f"  Output {output_dir}", flush=True)

def generate_bindings() -> None:
    run([
        "cargo",
        "run",
        "--manifest-path",
        str(RUST_WORKSPACE),
        "-p",
        "stelliberty_xtask",
        "--target",
        host_rust_target(),
        "--",
        "generate-bindings",
    ])


def host_rust_target() -> str:
    result = subprocess.run(
        ["rustc", "-vV"],
        capture_output=True,
        text=True,
        errors="replace",
        check=True,
    )
    for line in result.stdout.splitlines():
        key, separator, value = line.partition(":")
        if separator and key == "host":
            return value.strip()
    raise RuntimeError("Unable to read the Rust host target")


def build_rust(metadata: AppMetadata, configuration: str, target: PlatformTarget) -> None:
    command = [
        "cargo",
        "build",
        "--manifest-path",
        str(RUST_WORKSPACE),
        "-p",
        "hub",
        "-p",
        "stelliberty_service",
        "--target",
        target.rust_target,
    ]
    if configuration == "release":
        command.append("--release")

    # 服务版本固定为 crate 版本，不随 App 版本漂移。
    env = {"STELLIBERTY_APP_NAME": metadata.app_name}
    if configuration == "release":
        env.update(release_path_remap_env())

    run(command, env)

def publish_dotnet(metadata: AppMetadata, configuration: str, target: PlatformTarget, output_dir: Path) -> None:
    with tempfile.TemporaryDirectory(prefix="stelliberty-dotnet-publish-") as temp_dir:
        staging_dir = Path(temp_dir)
        tray_output = staging_dir / "tray"
        ui_output = staging_dir / "ui"
        publish_dotnet_project(TRAY_PROJECT, metadata, configuration, target, tray_output)
        publish_dotnet_project(DESKTOP_PROJECT, metadata, configuration, target, ui_output)
        output_dir.mkdir(parents=True, exist_ok=True)
        shutil.copytree(tray_output, output_dir, dirs_exist_ok=True)
        shutil.copytree(ui_output, output_dir, dirs_exist_ok=True)


def publish_dotnet_project(project: Path, metadata: AppMetadata, configuration: str, target: PlatformTarget, output_dir: Path) -> None:
    command = [
        "dotnet",
        "publish",
        str(project),
        "--configuration",
        dotnet_configuration(configuration),
        "--runtime",
        target.dotnet_runtime,
        "--self-contained",
        "false",
        "--output",
        str(output_dir),
        "--nologo",
        "--verbosity",
        "quiet",
        "-maxcpucount:1",
        f"/p:AppVersion={metadata.version}",
        f"/p:Version={metadata.version}",
    ]
    run(command)

def dotnet_configuration(configuration: str) -> str:
    return "Release" if configuration == "release" else "Debug"


def display_configuration(configuration: str) -> str:
    return "Release" if configuration == "release" else "Dev"

def reset_output_directory(output_dir: Path) -> None:
    resolved_output = output_dir.resolve()
    resolved_build = BUILD_DIR.resolve()
    if not resolved_output.is_relative_to(resolved_build):
        raise ValueError(f"Refusing to clean a path outside the build directory: {resolved_output}")

    if output_dir.exists():
        shutil.rmtree(output_dir)
        print(f"  Removed {output_dir}", flush=True)

def release_path_remap_env() -> dict[str, str]:
    separator = "\x1f"
    remaps = [
        rust_path_remap(ROOT, "."),
        rust_path_remap(Path.home(), ".home"),
    ]
    cargo_home = Path(os.environ.get("CARGO_HOME", Path.home() / ".cargo"))
    remaps.append(rust_path_remap(cargo_home, ".cargo"))
    current = os.environ.get("CARGO_ENCODED_RUSTFLAGS")
    encoded = separator.join(remaps) if not current else f"{current}{separator}{separator.join(remaps)}"
    return {"CARGO_ENCODED_RUSTFLAGS": encoded}

def rust_path_remap(source: Path, target: str) -> str:
    return f"--remap-path-prefix={source.resolve()}={target}"

def copy_native_library(configuration: str, target: PlatformTarget, output_dir: Path) -> None:
    profile = "release" if configuration == "release" else "debug"
    source = ROOT / "target" / target.rust_target / profile / target.native_library
    if not source.exists():
        raise FileNotFoundError(f"Native library not found: {source}")

    output_dir.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, output_dir / source.name)

def copy_service_binary(metadata: AppMetadata, configuration: str, target: PlatformTarget, output_dir: Path) -> None:
    profile = "release" if configuration == "release" else "debug"
    source = ROOT / "target" / target.rust_target / profile / target.service_binary
    if not source.exists():
        raise FileNotFoundError(f"Service binary not found: {source}")

    service_dir = output_dir / SERVICE_UPDATE_DIRECTORY
    reset_service_directory(service_dir)
    shutil.copy2(source, service_dir / service_binary_name(metadata, target))


def reset_service_directory(service_dir: Path) -> None:
    resolved_service = service_dir.resolve()
    resolved_build = BUILD_DIR.resolve()
    if not resolved_service.is_relative_to(resolved_build):
        raise ValueError(f"Refusing to clean a service path outside the build directory: {resolved_service}")

    if service_dir.exists():
        shutil.rmtree(service_dir)
    service_dir.mkdir(parents=True, exist_ok=True)


def copy_core_assets(platform_name: str, output_dir: Path) -> None:
    source = PRE_ASSETS_DIR / platform_name
    target = output_dir / CORE_DIRECTORY
    expected_names = expected_core_asset_names(platform_name)
    # 缺核心或 Geo 资源的产物运行时无法启动核心，宁可构建失败也不产出半成品。
    missing = sorted(name for name in expected_names if not (source / name).is_file())
    if missing:
        raise FileNotFoundError(
            f"Core assets missing from {source}: {', '.join(missing)}. Run scripts/prebuild.py to fetch them."
        )

    target.mkdir(parents=True, exist_ok=True)
    for name in sorted(expected_names):
        shutil.copy2(source / name, target / name)
    print(f"  Source {source}", flush=True)
    print(f"  Target {target}", flush=True)
    print(f"  Files {len(expected_names)}", flush=True)

def expected_core_asset_names(platform_name: str) -> set[str]:
    core_binary_name = "clash-mihomo-core.exe" if platform_name.startswith("win") else "clash-mihomo-core"
    return CORE_ASSET_NAMES | {core_binary_name}
