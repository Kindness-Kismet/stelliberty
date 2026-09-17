import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path
from xml.sax.saxutils import escape as xml_escape

from build_support.layout import display_version, service_binary_name
from build_support.models import AppMetadata, PlatformTarget
from build_support.paths import (
    DEPS_DIRECTORY,
    LINUX_INSTALLER_TEMPLATE_DIR,
    MACOS_INSTALLER_TEMPLATE_DIR,
    PACKAGES_DIR,
    ROOT,
    TOOLS_DIR,
    WINDOWS_INSTALLER_TEMPLATE,
)


INNO_SETUP_CANDIDATES = [
    Path(r"C:\Program Files\Inno Setup 7\ISCC.exe"),
    Path(r"C:\Program Files (x86)\Inno Setup 7\ISCC.exe"),
]


def pack_installers(
    metadata: AppMetadata,
    platform_name: str,
    configuration: str,
    target: PlatformTarget,
    output_dir: Path,
) -> list[Path]:
    check_packaging_tools(platform_name)

    if platform_name.startswith("win"):
        return [pack_windows_installer(metadata, platform_name, configuration, target, output_dir)]

    if platform_name.startswith("linux"):
        return pack_linux_installers(metadata, platform_name, configuration, target, output_dir)

    if platform_name.startswith("macos"):
        return pack_macos_installers(metadata, platform_name, configuration, target, output_dir)

    raise RuntimeError(f"Packaging is not supported for this platform yet: {platform_name}")


def check_packaging_tools(platform_name: str) -> None:
    if platform_name.startswith("win"):
        if sys.platform != "win32":
            raise RuntimeError("Windows packaging tools can only be checked on a Windows host")
        print(f"  Tool   {find_iscc()}", flush=True)
        return

    if platform_name.startswith("linux"):
        if not sys.platform.startswith("linux"):
            raise RuntimeError("Linux packaging tools can only be checked on a Linux host")
        for command in ("dpkg-deb", "rpmbuild", "tar"):
            ensure_command(command, f"Missing {command}")
            print(f"  Tool   {shutil.which(command)}", flush=True)
        zstd_path = find_tool("zstd")
        if zstd_path is None:
            raise RuntimeError("Missing zstd")
        print(f"  Tool   {zstd_path}", flush=True)
        print(f"  Tool   {find_appimagetool()}", flush=True)
        return

    if platform_name.startswith("macos"):
        if sys.platform != "darwin":
            raise RuntimeError("macOS packaging tools can only be checked on a macOS host")
        for command in ("hdiutil", "pkgbuild", "productbuild", "iconutil"):
            ensure_command(command, f"Missing {command}")
            print(f"  Tool   {shutil.which(command)}", flush=True)
        return

    raise RuntimeError(f"Packaging is not supported for this platform yet: {platform_name}")


def pack_windows_installer(
    metadata: AppMetadata,
    platform_name: str,
    configuration: str,
    target: PlatformTarget,
    output_dir: Path,
) -> Path:
    if sys.platform != "win32":
        raise RuntimeError("Inno Setup installers can only be created on a Windows host")

    if not platform_name.startswith("win"):
        raise RuntimeError(f"Installer packaging currently only supports Windows platforms: {platform_name}")

    iscc_path = find_iscc()
    package_base_name = installer_base_name(metadata, platform_name, configuration)
    iss_path = ROOT / "build" / "setup.iss"
    PACKAGES_DIR.mkdir(parents=True, exist_ok=True)
    iss_path.write_text(
        render_windows_template(metadata, platform_name, configuration, target, output_dir, package_base_name),
        encoding="utf-8-sig",
    )

    compile_inno_setup(iscc_path, iss_path)
    installer_path = PACKAGES_DIR / f"{package_base_name}.exe"
    if not installer_path.exists():
        raise FileNotFoundError(f"Installer was not generated: {installer_path}")

    return installer_path


def pack_linux_installers(
    metadata: AppMetadata,
    platform_name: str,
    configuration: str,
    target: PlatformTarget,
    output_dir: Path,
) -> list[Path]:
    if not sys.platform.startswith("linux"):
        raise RuntimeError("Linux packages can only be created on a Linux host")

    if not platform_name.startswith("linux"):
        raise RuntimeError(f"Linux packages do not support this platform: {platform_name}")

    PACKAGES_DIR.mkdir(parents=True, exist_ok=True)
    package_root = build_linux_package_root(metadata, platform_name, configuration, target, output_dir)
    try:
        base_name = linux_package_base_name(metadata, platform_name, configuration)
        return [
            pack_deb(metadata, platform_name, configuration, target, package_root, base_name),
            pack_rpm(metadata, platform_name, configuration, target, package_root, base_name),
            pack_arch_pkg(metadata, platform_name, configuration, target, package_root, base_name),
            pack_appimage(metadata, platform_name, configuration, target, output_dir, base_name),
        ]
    finally:
        shutil.rmtree(package_root, ignore_errors=True)


def pack_macos_installers(
    metadata: AppMetadata,
    platform_name: str,
    configuration: str,
    target: PlatformTarget,
    output_dir: Path,
) -> list[Path]:
    if sys.platform != "darwin":
        raise RuntimeError("macOS installers can only be created on a macOS host")

    if not platform_name.startswith("macos"):
        raise RuntimeError(f"macOS installers do not support this platform: {platform_name}")

    ensure_command("hdiutil", "Missing hdiutil; cannot create a DMG")
    ensure_command("pkgbuild", "Missing pkgbuild; cannot create the PKG component")
    ensure_command("productbuild", "Missing productbuild; cannot create the PKG")
    ensure_command("iconutil", "Missing iconutil; cannot create the macOS icon")

    PACKAGES_DIR.mkdir(parents=True, exist_ok=True)
    base_name = macos_package_base_name(metadata, platform_name, configuration)
    with tempfile.TemporaryDirectory(prefix=f"{linux_app_package(metadata)}-macos-package-") as temp_dir:
        app_path = build_macos_app_bundle(metadata, platform_name, configuration, target, output_dir, Path(temp_dir))
        return [
            pack_dmg(metadata, configuration, app_path, base_name),
            pack_pkg(metadata, configuration, app_path, base_name),
        ]


def build_macos_app_bundle(
    metadata: AppMetadata,
    platform_name: str,
    configuration: str,
    target: PlatformTarget,
    output_dir: Path,
    temp_dir: Path,
) -> Path:
    app_path = temp_dir / f"{display_name(metadata, configuration)}.app"
    contents_dir = app_path / "Contents"
    macos_dir = contents_dir / "MacOS"
    resources_dir = contents_dir / "Resources"

    shutil.copytree(output_dir, macos_dir)
    resources_dir.mkdir(parents=True, exist_ok=True)

    executable_path = macos_dir / metadata.app_name
    if not executable_path.exists():
        raise FileNotFoundError(f"macOS executable does not exist: {executable_path}")

    set_macos_payload_permissions(macos_dir, metadata, target)
    write_macos_info_plist(metadata, configuration, platform_name, contents_dir / "Info.plist")
    build_macos_icns(resources_dir / "AppIcon.icns", configuration)
    return app_path


def pack_dmg(metadata: AppMetadata, configuration: str, app_path: Path, base_name: str) -> Path:
    output_path = PACKAGES_DIR / f"{base_name}.dmg"
    run_checked([
        "hdiutil",
        "create",
        "-volname",
        display_name(metadata, configuration),
        "-srcfolder",
        str(app_path),
        "-ov",
        "-format",
        "UDZO",
        str(output_path),
    ])
    require_file(output_path)
    return output_path


def pack_pkg(metadata: AppMetadata, configuration: str, app_path: Path, base_name: str) -> Path:
    output_path = PACKAGES_DIR / f"{base_name}.pkg"
    identifier = macos_bundle_identifier(metadata, configuration)
    with tempfile.TemporaryDirectory(prefix=f"{linux_app_package(metadata)}-pkg-") as temp_dir:
        component_pkg = Path(temp_dir) / f"{identifier}-component.pkg"
        run_checked([
            "pkgbuild",
            "--component",
            str(app_path),
            "--install-location",
            "/Applications",
            "--identifier",
            identifier,
            "--version",
            metadata.version,
            str(component_pkg),
        ])
        run_checked([
            "productbuild",
            "--package",
            str(component_pkg),
            str(output_path),
        ])
    require_file(output_path)
    return output_path


def build_linux_package_root(
    metadata: AppMetadata,
    platform_name: str,
    configuration: str,
    target: PlatformTarget,
    output_dir: Path,
) -> Path:
    package_root = Path(tempfile.mkdtemp(prefix=f"{linux_app_package(metadata)}-linux-package-"))
    app_package = linux_app_package(metadata)
    install_dir = package_root / "opt" / app_package
    bin_dir = package_root / "usr" / "bin"
    applications_dir = package_root / "usr" / "share" / "applications"
    icons_dir = package_root / "usr" / "share" / "icons" / "hicolor" / "256x256" / "apps"

    shutil.copytree(output_dir, install_dir, dirs_exist_ok=True)
    bin_dir.mkdir(parents=True, exist_ok=True)
    applications_dir.mkdir(parents=True, exist_ok=True)
    icons_dir.mkdir(parents=True, exist_ok=True)

    replacements = linux_replacements(metadata, configuration, target, package_root, linux_arch(platform_name))
    write_rendered_template("launcher.in", bin_dir / app_package, replacements, executable=True)
    write_rendered_template("stelliberty.desktop.in", applications_dir / f"{app_package}.desktop", replacements | {"APP_EXEC": f"/opt/{app_package}/{metadata.app_name}"})
    copy_linux_icon(icons_dir / f"{app_package}.png", configuration)
    set_linux_payload_permissions(install_dir, metadata, target)
    return package_root


def pack_deb(
    metadata: AppMetadata,
    platform_name: str,
    configuration: str,
    target: PlatformTarget,
    package_root: Path,
    base_name: str,
) -> Path:
    ensure_command("dpkg-deb", "Missing dpkg-deb")
    output_path = PACKAGES_DIR / f"{base_name}.deb"
    with tempfile.TemporaryDirectory(prefix=f"{linux_app_package(metadata)}-deb-") as temp_dir:
        deb_root = Path(temp_dir) / linux_app_package(metadata)
        shutil.copytree(package_root, deb_root)
        debian_dir = deb_root / "DEBIAN"
        debian_dir.mkdir(parents=True, exist_ok=True)

        replacements = linux_replacements(metadata, configuration, target, package_root, linux_arch(platform_name))
        replacements["DEB_ARCH"] = deb_arch(platform_name)
        replacements["INSTALLED_SIZE"] = str(installed_size_kb(deb_root))
        write_rendered_template("control.in", debian_dir / "control", replacements)
        write_rendered_template("postinst.in", debian_dir / "postinst", replacements, executable=True)
        write_rendered_template("prerm.in", debian_dir / "prerm", replacements, executable=True)

        run_checked(["dpkg-deb", "--build", "--root-owner-group", str(deb_root), str(output_path)])
    require_file(output_path)
    return output_path


def pack_rpm(
    metadata: AppMetadata,
    platform_name: str,
    configuration: str,
    target: PlatformTarget,
    package_root: Path,
    base_name: str,
) -> Path:
    ensure_command("rpmbuild", "Missing rpmbuild")
    output_path = PACKAGES_DIR / f"{base_name}.rpm"
    with tempfile.TemporaryDirectory(prefix=f"{linux_app_package(metadata)}-rpm-") as temp_dir:
        rpm_root = Path(temp_dir)
        spec_dir = rpm_root / "SPECS"
        spec_dir.mkdir(parents=True, exist_ok=True)
        spec_path = spec_dir / f"{linux_app_package(metadata)}.spec"

        replacements = linux_replacements(metadata, configuration, target, package_root, linux_arch(platform_name))
        replacements["APP_VERSION"] = rpm_package_version(metadata.version)
        replacements["RPM_ARCH"] = rpm_arch(platform_name)
        replacements["PACKAGE_ROOT"] = sh_path(package_root)
        write_rendered_template("rpm.spec.in", spec_path, replacements)

        run_checked(["rpmbuild", "-bb", "--define", f"_topdir {rpm_root}", str(spec_path)])
        rpm_files = sorted((rpm_root / "RPMS").rglob("*.rpm"))
        if not rpm_files:
            raise FileNotFoundError("RPM package was not generated")
        shutil.copy2(rpm_files[0], output_path)
    require_file(output_path)
    return output_path


def pack_arch_pkg(
    metadata: AppMetadata,
    platform_name: str,
    configuration: str,
    target: PlatformTarget,
    package_root: Path,
    base_name: str,
) -> Path:
    ensure_command("tar", "Missing tar; cannot create the Arch package")
    zstd_path = find_tool("zstd")
    if zstd_path is None:
        raise RuntimeError("Missing zstd")

    output_path = PACKAGES_DIR / f"{base_name}.pkg.tar.zst"
    with tempfile.TemporaryDirectory(prefix=f"{linux_app_package(metadata)}-arch-") as temp_dir:
        arch_root = Path(temp_dir) / "pkg"
        shutil.copytree(package_root, arch_root)

        replacements = linux_replacements(metadata, configuration, target, package_root, linux_arch(platform_name))
        replacements["ARCH_VERSION"] = f"{arch_package_version(metadata.version)}-1"
        replacements["ARCH_PKG_ARCH"] = arch_pkg_arch(platform_name)
        replacements["BUILD_DATE"] = str(int(time.time()))
        replacements["INSTALLED_SIZE_BYTES"] = str(directory_size(arch_root))
        write_rendered_template("arch.PKGINFO.in", arch_root / ".PKGINFO", replacements)
        write_rendered_template("arch.INSTALL.in", arch_root / ".INSTALL", replacements)

        run_checked([
            "tar",
            "--zstd",
            "--owner=0",
            "--group=0",
            "--numeric-owner",
            "-cf",
            str(output_path),
            "-C",
            str(arch_root),
            ".PKGINFO",
            ".INSTALL",
            "opt",
            "usr",
        ], env=path_env(zstd_path.parent))
    require_file(output_path)
    return output_path


def pack_appimage(
    metadata: AppMetadata,
    platform_name: str,
    configuration: str,
    target: PlatformTarget,
    output_dir: Path,
    base_name: str,
) -> Path:
    appimagetool = find_appimagetool()
    output_path = PACKAGES_DIR / f"{base_name}.AppImage"
    app_package = linux_app_package(metadata)
    with tempfile.TemporaryDirectory(prefix=f"{linux_app_package(metadata)}-appimage-") as temp_dir:
        app_dir = Path(temp_dir) / f"{file_name_part(display_name(metadata, configuration))}.AppDir"
        app_payload_dir = app_dir / "usr" / "lib" / app_package
        applications_dir = app_dir / "usr" / "share" / "applications"
        icons_dir = app_dir / "usr" / "share" / "icons" / "hicolor" / "256x256" / "apps"

        shutil.copytree(output_dir, app_payload_dir, dirs_exist_ok=True)
        applications_dir.mkdir(parents=True, exist_ok=True)
        icons_dir.mkdir(parents=True, exist_ok=True)

        replacements = linux_replacements(metadata, configuration, target, app_dir, linux_arch(platform_name))
        write_rendered_template("appimage.AppRun.in", app_dir / "AppRun", replacements, executable=True)
        write_rendered_template("stelliberty.desktop.in", app_dir / f"{app_package}.desktop", replacements | {"APP_EXEC": metadata.app_name})
        write_rendered_template("stelliberty.desktop.in", applications_dir / f"{app_package}.desktop", replacements | {"APP_EXEC": metadata.app_name})
        copy_linux_icon(app_dir / f"{app_package}.png", configuration)
        copy_linux_icon(icons_dir / f"{app_package}.png", configuration)
        set_linux_payload_permissions(app_payload_dir, metadata, target)

        env = os.environ.copy()
        env["ARCH"] = appimage_arch(platform_name)
        env["APPIMAGE_EXTRACT_AND_RUN"] = "1"
        run_checked([str(appimagetool), str(app_dir), str(output_path)], env=env)
    require_file(output_path)
    return output_path


def compile_inno_setup(iscc_path: Path, iss_path: Path) -> None:
    result = subprocess.run(
        [str(iscc_path), "/Q", str(iss_path)],
        cwd=ROOT,
        capture_output=True,
        text=True,
        errors="replace",
        check=False,
    )
    if result.returncode == 0:
        return

    if result.stdout:
        print(result.stdout.rstrip(), flush=True)
    if result.stderr:
        print(result.stderr.rstrip(), flush=True)
    result.check_returncode()


def find_iscc() -> Path:
    for path in INNO_SETUP_CANDIDATES:
        if path.exists() and is_inno_setup_7(path):
            return path

    for command in ("ISCC.exe", "iscc"):
        resolved = shutil.which(command)
        if resolved is None:
            continue
        path = Path(resolved)
        if is_inno_setup_7(path):
            return path

    raise FileNotFoundError("Inno Setup 7 compiler ISCC.exe was not found")


def is_inno_setup_7(path: Path) -> bool:
    version = iscc_version(path)
    return version is not None and (version == "7" or version.startswith("7."))


def iscc_version(path: Path) -> str | None:
    try:
        result = subprocess.run(
            [str(path), "/?"],
            capture_output=True,
            text=True,
            errors="replace",
            timeout=10,
            check=False,
        )
    except (OSError, subprocess.SubprocessError):
        return None

    output = f"{result.stdout}\n{result.stderr}"
    match = re.search(r"Compiler engine version:\s*Inno Setup\s+([^\r\n]+)", output)
    if match:
        return match.group(1).strip()

    if "Inno Setup 7 Command-Line Compiler" in output:
        return registry_inno_version(path) or "7"

    return None


def registry_inno_version(path: Path) -> str | None:
    if sys.platform != "win32":
        return None

    try:
        import winreg
    except ImportError:
        return None

    uninstall_roots = (
        r"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        r"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    )
    target_path = path.resolve()
    for root_name in uninstall_roots:
        version = read_registry_inno_version(winreg.HKEY_LOCAL_MACHINE, root_name, target_path)
        if version:
            return version
    return None


def read_registry_inno_version(root, root_name: str, target_path: Path) -> str | None:
    import winreg

    try:
        root_key = winreg.OpenKey(root, root_name)
    except OSError:
        return None

    with root_key:
        for index in range(winreg.QueryInfoKey(root_key)[0]):
            try:
                subkey_name = winreg.EnumKey(root_key, index)
                with winreg.OpenKey(root_key, subkey_name) as subkey:
                    display_name = winreg.QueryValueEx(subkey, "DisplayName")[0]
                    install_location = winreg.QueryValueEx(subkey, "InstallLocation")[0]
                    display_version = winreg.QueryValueEx(subkey, "DisplayVersion")[0]
            except OSError:
                continue

            if not str(display_name).startswith("Inno Setup"):
                continue

            install_path = Path(str(install_location)).resolve()
            if target_path.is_relative_to(install_path):
                return str(display_version).strip()

    return None


def find_appimagetool() -> Path:
    if tool_path := find_tool("appimagetool"):
        return tool_path
    raise RuntimeError("Missing appimagetool")


def display_name(metadata: AppMetadata, configuration: str) -> str:
    if configuration == "dev":
        return f"{metadata.display_name} Dev"
    return metadata.display_name


def installer_base_name(metadata: AppMetadata, platform_name: str, configuration: str) -> str:
    parts = [
        metadata.app_name,
        display_version(metadata.version),
        platform_name,
    ]
    if configuration != "release":
        parts.append("dev")
    parts.append("setup")
    return "-".join(parts)


def render_windows_template(
    metadata: AppMetadata,
    platform_name: str,
    configuration: str,
    target: PlatformTarget,
    output_dir: Path,
    output_base_name: str,
) -> str:
    language_dir = ROOT / "scripts" / "installer" / "windows" / "languages"
    for language_file in ("ChineseSimplified.isl", "ChineseTraditional.isl"):
        if not (language_dir / language_file).exists():
            raise FileNotFoundError(f"Missing Inno Setup language file: {language_dir / language_file}")

    replacements = {
        "APP_NAME": display_name(metadata, configuration),
        "APP_VERSION": metadata.version,
        "APP_PUBLISHER": metadata.display_name,
        "APP_EXE_NAME": f"{metadata.app_name}.exe",
        "APP_UI_EXE_NAME": str(DEPS_DIRECTORY / f"{metadata.app_name}_ui.exe").replace("/", "\\"),
        "APP_PACKAGE_NAME": metadata.app_name.lower(),
        "APP_MUTEX": single_instance_mutex(metadata, configuration),
        "APP_ID": app_id(configuration),
        "SERVICE_NAME": service_name(metadata, configuration),
        "SERVICE_BINARY": service_binary_name(metadata, target),
        "OUTPUT_DIR": iss_path(PACKAGES_DIR),
        "OUTPUT_BASE_FILENAME": output_base_name,
        "SOURCE_DIR": iss_path(output_dir),
        "LANGUAGE_DIR": iss_path(language_dir),
        "ARCH_MODE": arch_mode(platform_name),
    }
    template = WINDOWS_INSTALLER_TEMPLATE.read_text(encoding="utf-8")
    for key, value in replacements.items():
        template = template.replace(f"@{key}@", value)
    return template


def single_instance_mutex(metadata: AppMetadata, configuration: str) -> str:
    channel = "Dev" if configuration == "dev" else "Release"
    return f"Global\\{metadata.app_name}.{channel}.SingleInstance"


def app_id(configuration: str) -> str:
    if configuration == "dev":
        return "72E76315-0D0E-4D50-8BD9-7254734F27B4"
    return "B53A7C5D-F22A-4B4F-B255-6C7C5C687780"


def service_name(metadata: AppMetadata, configuration: str) -> str:
    suffix = "Dev" if configuration == "dev" else ""
    return f"{pascal_identifier(metadata.app_name)}Service{suffix}"


def pascal_identifier(value: str) -> str:
    output: list[str] = []
    capitalize_next = True
    for ch in value:
        if not ch.isascii() or not ch.isalnum():
            capitalize_next = True
            continue

        output.append(ch.upper() if capitalize_next else ch)
        capitalize_next = False

    return "".join(output) or "App"


def arch_mode(platform_name: str) -> str:
    if platform_name == "win-x64":
        return "ArchitecturesInstallIn64BitMode=x64compatible"
    if platform_name == "win-arm64":
        return "ArchitecturesAllowed=arm64\nArchitecturesInstallIn64BitMode=arm64"
    raise RuntimeError(f"Installer packaging does not support this Windows architecture yet: {platform_name}")


def iss_path(path: Path) -> str:
    return str(path.resolve()).replace("/", "\\").replace('"', '""')


def file_name_part(value: str) -> str:
    normalized = re.sub(r"[^A-Za-z0-9]+", "-", value.strip())
    return normalized.strip("-")


def linux_package_base_name(metadata: AppMetadata, platform_name: str, configuration: str) -> str:
    parts = [metadata.app_name, display_version(metadata.version), platform_name]
    if configuration != "release":
        parts.append("dev")
    return "-".join(parts)


def rpm_package_version(version: str) -> str:
    return version.replace("-", "_").lstrip("vV")


def arch_package_version(version: str) -> str:
    return version.replace("-", "_").lstrip("vV")


def macos_package_base_name(metadata: AppMetadata, platform_name: str, configuration: str) -> str:
    parts = [metadata.app_name, display_version(metadata.version), platform_name]
    if configuration != "release":
        parts.append("dev")
    return "-".join(parts)


def linux_app_package(metadata: AppMetadata) -> str:
    return re.sub(r"[^a-z0-9+.-]+", "-", metadata.app_name.lower()).strip("-") or "stelliberty"


def linux_arch(platform_name: str) -> str:
    if platform_name.endswith("-x64"):
        return "x64"
    if platform_name.endswith("-arm64"):
        return "arm64"
    raise RuntimeError(f"Unsupported Linux architecture: {platform_name}")


def deb_arch(platform_name: str) -> str:
    return {
        "x64": "amd64",
        "arm64": "arm64",
    }[linux_arch(platform_name)]


def rpm_arch(platform_name: str) -> str:
    return {
        "x64": "x86_64",
        "arm64": "aarch64",
    }[linux_arch(platform_name)]


def arch_pkg_arch(platform_name: str) -> str:
    return {
        "x64": "x86_64",
        "arm64": "aarch64",
    }[linux_arch(platform_name)]


def appimage_arch(platform_name: str) -> str:
    return {
        "x64": "x86_64",
        "arm64": "aarch64",
    }[linux_arch(platform_name)]


def linux_replacements(
    metadata: AppMetadata,
    configuration: str,
    target: PlatformTarget,
    package_root: Path,
    arch: str,
) -> dict[str, str]:
    app_package = linux_app_package(metadata)
    return {
        "APP_NAME": display_name(metadata, configuration),
        "APP_VERSION": metadata.version,
        "APP_PACKAGE": app_package,
        "APP_EXECUTABLE": metadata.app_name,
        "SERVICE_BINARY": service_binary_name(metadata, target),
        "LINUX_ARCH": arch,
        "PACKAGE_ROOT": sh_path(package_root),
    }


def write_rendered_template(
    template_name: str,
    output_path: Path,
    replacements: dict[str, str],
    executable: bool = False,
) -> None:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    content = render_template(LINUX_INSTALLER_TEMPLATE_DIR / template_name, replacements)
    output_path.write_text(content, encoding="utf-8", newline="\n")
    if executable:
        output_path.chmod(0o755)


def write_macos_info_plist(
    metadata: AppMetadata,
    configuration: str,
    platform_name: str,
    output_path: Path,
) -> None:
    replacements = {
        "APP_NAME": xml_escape(display_name(metadata, configuration)),
        "APP_EXECUTABLE": xml_escape(metadata.app_name),
        "APP_IDENTIFIER": xml_escape(macos_bundle_identifier(metadata, configuration)),
        "APP_VERSION": xml_escape(metadata.version),
        "APP_BUILD": xml_escape(macos_build_version(metadata.version)),
        "APP_MIN_SYSTEM": macos_min_system(platform_name),
    }
    output_path.write_text(render_macos_template("Info.plist.in", replacements), encoding="utf-8", newline="\n")


def render_macos_template(template_name: str, replacements: dict[str, str]) -> str:
    content = (MACOS_INSTALLER_TEMPLATE_DIR / template_name).read_text(encoding="utf-8")
    for key, value in replacements.items():
        content = content.replace(f"@{key}@", value)
    return content


def render_template(template_path: Path, replacements: dict[str, str]) -> str:
    content = template_path.read_text(encoding="utf-8")
    for key, value in replacements.items():
        content = content.replace(f"@{key}@", value)
    return content


def copy_linux_icon(target_path: Path, configuration: str) -> None:
    icon_name = "app_icon.png"
    source = ROOT / "src" / "Stelliberty.Desktop" / "Assets" / "linux" / icon_name
    if not source.exists():
        raise FileNotFoundError(f"Linux icon does not exist: {source}")
    target_path.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, target_path)


def build_macos_icns(output_path: Path, configuration: str) -> None:
    icon_prefix = "app_icon"
    icon_sources = {
        "icon_16x16.png": "app_icon_16.png",
        "icon_16x16@2x.png": "app_icon_32.png",
        "icon_32x32.png": "app_icon_32.png",
        "icon_32x32@2x.png": "app_icon_64.png",
        "icon_128x128.png": "app_icon_128.png",
        "icon_128x128@2x.png": "app_icon_256.png",
        "icon_256x256.png": "app_icon_256.png",
        "icon_256x256@2x.png": "app_icon_512.png",
        "icon_512x512.png": "app_icon_512.png",
        "icon_512x512@2x.png": "app_icon_1024.png",
    }
    source_dir = ROOT / "src" / "Stelliberty.Desktop" / "Assets" / "macos"
    with tempfile.TemporaryDirectory(prefix="app-iconset-") as temp_dir:
        iconset_dir = Path(temp_dir) / "AppIcon.iconset"
        iconset_dir.mkdir(parents=True, exist_ok=True)
        for target_name, source_name in icon_sources.items():
            source_path = source_dir / source_name.replace("app_icon", icon_prefix)
            if not source_path.exists():
                raise FileNotFoundError(f"macOS icon does not exist: {source_path}")
            shutil.copy2(source_path, iconset_dir / target_name)
        run_checked(["iconutil", "-c", "icns", str(iconset_dir), "-o", str(output_path)])


def set_macos_payload_permissions(install_dir: Path, metadata: AppMetadata, target: PlatformTarget) -> None:
    executable_paths = [
        install_dir / metadata.app_name,
        install_dir / DEPS_DIRECTORY / f"{metadata.app_name}_ui",
        install_dir / "data" / "core" / "clash-mihomo-core",
        install_dir / "data" / "service" / "update" / service_binary_name(metadata, target),
    ]
    for path in executable_paths:
        if path.exists():
            path.chmod(path.stat().st_mode | 0o755)

    data_dir = install_dir / "data"
    if data_dir.exists():
        for path in [data_dir, *data_dir.rglob("*")]:
            writable_mode = 0o777 if path.is_dir() else 0o666
            path.chmod(path.stat().st_mode | writable_mode)


def macos_bundle_identifier(metadata: AppMetadata, configuration: str = "release") -> str:
    suffix = ".dev" if configuration == "dev" else ""
    package = linux_app_package(metadata)
    return f"com.{package}.{package}{suffix}"


def macos_build_version(version: str) -> str:
    match = re.search(r"\d+(?:\.\d+)*", version)
    return match.group(0) if match else "1"


def macos_min_system(platform_name: str) -> str:
    if platform_name.startswith("macos"):
        return "13.0"
    raise RuntimeError(f"Unsupported macOS platform: {platform_name}")


def set_linux_payload_permissions(install_dir: Path, metadata: AppMetadata, target: PlatformTarget) -> None:
    executable_paths = [
        install_dir / metadata.app_name,
        install_dir / DEPS_DIRECTORY / f"{metadata.app_name}_ui",
        install_dir / "data" / "core" / "clash-mihomo-core",
        install_dir / "data" / "service" / "update" / service_binary_name(metadata, target),
    ]
    for path in executable_paths:
        if path.exists():
            path.chmod(path.stat().st_mode | 0o755)


def installed_size_kb(path: Path) -> int:
    return max(1, (directory_size(path) + 1023) // 1024)


def directory_size(path: Path) -> int:
    return sum(entry.stat().st_size for entry in path.rglob("*") if entry.is_file())


def ensure_command(command: str, message: str) -> None:
    if shutil.which(command) is None:
        raise RuntimeError(message)


def find_tool(command: str) -> Path | None:
    resolved = shutil.which(command)
    if resolved:
        return Path(resolved)

    tool_path = TOOLS_DIR / command
    if is_executable_file(tool_path):
        return tool_path
    return None


def path_env(directory: Path) -> dict[str, str]:
    env = os.environ.copy()
    env["PATH"] = f"{directory}{os.pathsep}{env.get('PATH', '')}"
    return env


def run_checked(command: list[str], env: dict[str, str] | None = None) -> None:
    # 打包工具的报错是排查唯一线索，严格解码会让它变 None 而只剩一个退出码。
    result = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True, errors="replace", check=False)
    if result.returncode == 0:
        return

    if result.stdout:
        print(result.stdout.rstrip(), flush=True)
    if result.stderr:
        print(result.stderr.rstrip(), flush=True)
    result.check_returncode()


def require_file(path: Path) -> None:
    if not path.exists():
        raise FileNotFoundError(f"Package was not generated: {path}")
    print(f"  Package {path}", flush=True)


def is_executable_file(path: Path) -> bool:
    return path.is_file() and os.access(path, os.X_OK)


def sh_path(path: Path) -> str:
    return str(path.resolve()).replace('"', '\\"')
