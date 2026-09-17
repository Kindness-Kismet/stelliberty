import json
import shutil
from pathlib import Path

from build_support.models import AppMetadata
from build_support.models import PlatformTarget
from build_support.paths import DEPS_DIRECTORY

HUB_LIBRARY_NAMES = {"hub.dll", "libhub.so", "libhub.dylib"}
HUB_SYMBOL_NAMES = {"hub.pdb", "libhub.pdb"}
DEPENDENCY_SUFFIXES = {".dll", ".dylib", ".pdb", ".so"}
HOST_STARTUP_FILE_NAMES = {
    "createdump",
    "createdump.exe",
    "hostfxr.dll",
    "hostpolicy.dll",
    "libhostfxr.dylib",
    "libhostfxr.so",
    "libhostpolicy.dylib",
    "libhostpolicy.so",
}


def output_name(metadata: AppMetadata, platform_name: str, configuration: str) -> str:
    parts = [metadata.app_name, display_version(metadata.version), platform_name]
    if configuration != "release":
        parts.append("dev")
    return "-".join(parts)


def service_binary_name(metadata: AppMetadata, target: PlatformTarget) -> str:
    suffix = ".exe" if target.service_binary.lower().endswith(".exe") else ""
    return f"{file_token(metadata.app_name)}_service{suffix}"


def file_token(value: str) -> str:
    output: list[str] = []
    previous_was_separator = False
    for ch in value:
        if ch.isascii() and ch.isalnum():
            output.append(ch.lower())
            previous_was_separator = False
            continue

        if not previous_was_separator:
            output.append("_")
            previous_was_separator = True

    token = "".join(output).strip("_")
    return token or "app"


def display_version(version: str) -> str:
    return version if version.lower().startswith("v") else f"v{version}"


def zip_output(output_dir: Path) -> Path:
    archive_path = output_dir.parent / f"{output_dir.name}.zip"
    if archive_path.exists():
        archive_path.unlink()

    shutil.make_archive(str(output_dir), "zip", output_dir)
    return archive_path


def organize_dependency_directory(output_dir: Path, metadata: AppMetadata, configuration: str) -> None:
    deps_dir = output_dir / DEPS_DIRECTORY
    deps_dir.mkdir(parents=True, exist_ok=True)

    remove_release_symbols(output_dir, configuration)
    move_ui_host_files(output_dir, deps_dir, metadata)
    moved_files = move_dependency_files(output_dir, deps_dir, root_files(metadata), configuration)
    rewrite_dependency_manifest(output_dir / f"{metadata.app_name}.deps.json", moved_files)


def root_files(metadata: AppMetadata) -> set[str]:
    tray_name = metadata.app_name
    return HOST_STARTUP_FILE_NAMES | {
        tray_name,
        f"{tray_name}.dll",
        f"{tray_name}.exe",
        f"{tray_name}.deps.json",
        f"{tray_name}.pdb",
        f"{tray_name}.runtimeconfig.json",
    }


def move_ui_host_files(output_dir: Path, deps_dir: Path, metadata: AppMetadata) -> None:
    ui_name = f"{metadata.app_name}_ui"
    file_names = {
        ui_name,
        f"{ui_name}.dll",
        f"{ui_name}.exe",
        f"{ui_name}.deps.json",
        f"{ui_name}.pdb",
        f"{ui_name}.runtimeconfig.json",
    }
    for file_name in file_names:
        source = output_dir / file_name
        if not source.exists():
            continue

        target = deps_dir / file_name
        if target.exists():
            target.unlink()
        shutil.move(str(source), target)


def remove_release_symbols(output_dir: Path, configuration: str) -> None:
    if configuration != "release":
        return

    for path in output_dir.glob("*.pdb"):
        path.unlink()


def move_dependency_files(output_dir: Path, deps_dir: Path, root_file_names: set[str], configuration: str) -> set[str]:
    moved_files: set[str] = set()

    for path in output_dir.iterdir():
        if path.name in root_file_names or path.is_dir() or not should_move_dependency_file(path, configuration):
            continue

        target = deps_dir / path.name
        if target.exists():
            target.unlink()
        shutil.move(str(path), target)
        moved_files.add(path.name)

    return moved_files


def should_move_dependency_file(path: Path, configuration: str) -> bool:
    if path.suffix.lower() in DEPENDENCY_SUFFIXES:
        return True

    if is_versioned_shared_object(path):
        return True

    if path.name in HUB_LIBRARY_NAMES:
        return True

    if configuration != "release" and path.name in HUB_SYMBOL_NAMES:
        return True

    return False


def is_versioned_shared_object(path: Path) -> bool:
    name = path.name
    return name.startswith("lib") and ".so." in name


def rewrite_dependency_manifest(deps_path: Path, moved_files: set[str]) -> None:
    if not moved_files or not deps_path.exists():
        return

    data = json.loads(deps_path.read_text(encoding="utf-8"))
    for runtime_target in data.get("targets", {}).values():
        for package in runtime_target.values():
            for section_name in ("runtime", "native"):
                rewrite_dependency_section(package, section_name, moved_files)

    deps_path.write_text(json.dumps(data, indent=2), encoding="utf-8")


def rewrite_dependency_section(package: dict, section_name: str, moved_files: set[str]) -> None:
    section = package.get(section_name)
    if not isinstance(section, dict):
        return

    replacements = {}
    for key, value in list(section.items()):
        file_name = Path(key).name
        if file_name not in moved_files:
            continue

        replacements[str(DEPS_DIRECTORY / file_name).replace("\\", "/")] = value
        del section[key]

    section.update(replacements)
