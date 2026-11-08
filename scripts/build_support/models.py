from dataclasses import dataclass


@dataclass(frozen=True)
class PlatformTarget:
    dotnet_runtime: str
    rust_target: str
    native_library: str
    service_binary: str


@dataclass(frozen=True)
class AppMetadata:
    app_name: str
    display_name: str
    version: str

    def with_version(self, version: str) -> "AppMetadata":
        return AppMetadata(self.app_name, self.display_name, version)


@dataclass(frozen=True)
class BuildRequest:
    configurations: list[str]
    platforms: list[tuple[str, PlatformTarget]]
    metadata: AppMetadata
    pack_format: str | None
    clean: bool
