using Stelliberty.Application.Platform;

namespace Stelliberty.Tray;

internal static class TrayApplicationLayout
{
    private static string BaseDirectory => AppContext.BaseDirectory;

    private static string InstallDataDirectory => Path.Combine(BaseDirectory, PathConventions.DataDirectoryName);

    public static string DepsDirectory => Path.Combine(
        InstallDataDirectory,
        PathConventions.DepsSubdirectory);

    public static string AppDataDirectory => OperatingSystem.IsMacOS()
        ? PortableDataDirectoryResolver.ResolveMacOS(BaseDirectory)
        : OperatingSystem.IsLinux()
            ? PortableDataDirectoryResolver.ResolveLinux(
                BaseDirectory,
                Environment.GetEnvironmentVariable(PathConventions.PortableDataDirectoryEnvironmentVariable))
            : InstallDataDirectory;

    public static string CoreDirectory => Path.Combine(InstallDataDirectory, PathConventions.CoreSubdirectory);

    public static string CoreBinaryPath => Path.Combine(
        CoreDirectory,
        OperatingSystem.IsWindows() ? "clash-mihomo-core.exe" : "clash-mihomo-core");

    public static string RuntimeDirectory => Path.Combine(AppDataDirectory, PathConventions.RuntimeSubdirectory);

    public static string ServiceDirectory => Path.Combine(AppDataDirectory, PathConventions.ServiceSubdirectory);

    public static string ServiceUpdateDirectory => Path.Combine(
        InstallDataDirectory,
        PathConventions.ServiceSubdirectory,
        PathConventions.ServiceUpdateSubdirectory);

    public static string ServiceUpdateBinaryPath => Path.Combine(
        ServiceUpdateDirectory,
        AppRuntimeNames.ServiceBinaryName);

    public static string ServiceInstalledBinaryPath => Path.Combine(
        ServiceDirectory,
        OperatingSystem.IsWindows()
            ? $"{PathConventions.ServiceInstalledBinaryStem}.exe"
            : PathConventions.ServiceInstalledBinaryStem);

    public static string SettingsFilePath => Path.Combine(AppDataDirectory, PathConventions.SettingsFileName);

    public static string RunningLogFilePath => Path.Combine(
        AppDataDirectory,
        PathConventions.AppLogsSubdirectory,
        "tray-running.logs");
}
