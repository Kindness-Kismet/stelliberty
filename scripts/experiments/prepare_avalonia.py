import subprocess
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
EXPERIMENT = ROOT / "build/experiments/avalonia-hidden-memory"
SOURCE = EXPERIMENT / "Avalonia"
COMMIT = "2d79ba55fae1b1e71c69f93a668080c9029e6a31"
PATCH = Path(__file__).with_name("avalonia-hidden-memory.patch.txt")


def run(*command: str) -> None:
    subprocess.run(command, cwd=ROOT, check=True)


def main() -> None:
    EXPERIMENT.mkdir(parents=True, exist_ok=True)
    if not SOURCE.exists():
        run("git", "clone", "--depth", "1", "--branch", "12.1.4",
            "https://github.com/AvaloniaUI/Avalonia.git", str(SOURCE))
    commit = subprocess.check_output(["git", "-C", str(SOURCE), "rev-parse", "HEAD"], text=True).strip()
    if commit != COMMIT:
        raise RuntimeError(f"Expected Avalonia {COMMIT}, found {commit}")
    run("git", "-C", str(SOURCE), "submodule", "update", "--init", "--depth", "1", "external/XamlX")
    applied = subprocess.run(["git", "-C", str(SOURCE), "apply", "--unidiff-zero", "--reverse", "--check", str(PATCH)],
                             stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode == 0
    if not applied:
        run("git", "-C", str(SOURCE), "apply", "--unidiff-zero", "--check", str(PATCH))
        run("git", "-C", str(SOURCE), "apply", "--intent-to-add", "--unidiff-zero", str(PATCH))

    sdk = EXPERIMENT / "dotnet10"
    if not (sdk / "dotnet.exe").is_file():
        archive = EXPERIMENT / "dotnet-sdk-10.0.201-win-x64.zip"
        if not zipfile.is_zipfile(archive):
            run("curl.exe", "--fail", "--location", "--retry", "3", "--connect-timeout", "15",
                "--speed-time", "30", "--speed-limit", "1024", "--output", str(archive),
                "https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.201/dotnet-sdk-10.0.201-win-x64.zip")
        with zipfile.ZipFile(archive) as package:
            package.extractall(sdk)
    run(str(sdk / "dotnet.exe"), "--version")
    print(f"Ready: {SOURCE}")
    print("python scripts/build.py --platform win-x64 --pack zip --avalonia-source build/experiments/avalonia-hidden-memory/Avalonia")


if __name__ == "__main__":
    main()
