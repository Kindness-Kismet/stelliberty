import hashlib
import json
import os
import shutil
import subprocess
from pathlib import Path

from build_support.commands import run
from build_support.models import BuildRequest

AVALONIA_COMMIT = "2d79ba55fae1b1e71c69f93a668080c9029e6a31"
FORK_REPOSITORY = "https://github.com/Kindness-Kismet/Avalonia"
FORK_BRANCH = "feat/explicit-graphics-resource-release"
FORK_COMMIT = "b2b6d67cba2ca42705a80709280f461b79ecc870"
PROJECTS = {
    "Avalonia.Base": "src/Avalonia.Base",
    "Avalonia.HarfBuzz": "src/HarfBuzz/Avalonia.HarfBuzz",
    "Avalonia.Skia": "src/Skia/Avalonia.Skia",
    "Avalonia.Win32": "src/Windows/Avalonia.Win32",
}


def build_avalonia_source(request: BuildRequest) -> Path:
    if any(name != "win-x64" for name, _ in request.platforms) or request.pack_format not in (None, "zip"):
        raise ValueError("Local Avalonia builds support Windows x64 with optional ZIP packaging")
    source = Path(request.avalonia_source).resolve()
    commit = subprocess.check_output(["git", "-C", str(source), "rev-parse", "HEAD"], text=True).strip()
    if commit != AVALONIA_COMMIT:
        raise ValueError(f"Expected Avalonia 12.1.4 ({AVALONIA_COMMIT}), found {commit}")
    patch = Path(__file__).resolve().parents[1] / "experiments/avalonia-hidden-memory.patch.txt"
    subprocess.run(["git", "-C", str(source), "apply", "--unidiff-zero", "--reverse", "--check", str(patch)], check=True)
    dotnet = source.parent / "dotnet10/dotnet.exe"
    if not dotnet.is_file():
        raise FileNotFoundError(f"Install the upstream .NET 10.0.201 SDK locally at {dotnet.parent}")
    for name, directory in PROJECTS.items():
        run([
            str(dotnet), "build", str(source / directory / f"{name}.csproj"),
            "--configuration", "Release", "--nologo", "--verbosity", "minimal", "-maxcpucount:1",
            "/p:AvsSkipBuildingLegacyTargetFrameworks=True",
            "/p:DisableSourceLink=true", "/p:NuGetAudit=false", "/p:TreatWarningsAsErrors=false",
        ], {"PATH": str(dotnet.parent) + os.pathsep + os.environ["PATH"]})
    return source


def copy_avalonia_source(source: Path | None, output: Path) -> None:
    manifest: dict = {"enabled": source is not None, "assemblies": {}}
    if source is not None:
        diff = subprocess.check_output(["git", "-C", str(source), "diff", "--binary", "HEAD"])
        manifest.update(source=str(source), base_commit=AVALONIA_COMMIT,
                        fork_repository=FORK_REPOSITORY, fork_branch=FORK_BRANCH, fork_commit=FORK_COMMIT,
                        patch_sha256=hashlib.sha256(diff).hexdigest())
        # 分支优化已移植到同版本源码，保留 NuGet 依赖清单和程序集公钥。
        for name, directory in PROJECTS.items():
            binary = source / directory / "bin/Release/net10.0" / f"{name}.dll"
            shutil.copy2(binary, output / binary.name)
    for name in PROJECTS:
        binary = output / f"{name}.dll"
        if binary.is_file():
            manifest["assemblies"][name] = hashlib.sha256(binary.read_bytes()).hexdigest()
    (output / "avalonia-source.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
