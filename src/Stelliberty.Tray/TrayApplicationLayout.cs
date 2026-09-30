using Stelliberty.Application.Platform;

namespace Stelliberty.Tray;

internal static class TrayApplicationLayout
{
    private static string BaseDirectory => AppContext.BaseDirectory;

    private static string InstallDataDirectory => Path.Combine(BaseDirectory, PathConventions.DataDirectoryName);

    public static string DepsDirectory => Path.Combine(
        InstallDataDirectory,
        PathConventions.DepsSubdirectory);

    public static string AppDataDirectory => AppDataDirectoryResolver.Resolve(
        BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        isPortable: OperatingSystem.IsWindows());

    // 安装载体随包分发的核心与 Geo 资源，只读。
    public static string InstallCoreDirectory => Path.Combine(InstallDataDirectory, PathConventions.CoreSubdirectory);

    // mihomo 工作目录与实际运行的核心；Windows 便携安装时与 InstallCoreDirectory 重合。
    public static string CoreDirectory => Path.Combine(AppDataDirectory, PathConventions.CoreSubdirectory);

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
        PathConventions.TrayRunningLogFileName);
}
