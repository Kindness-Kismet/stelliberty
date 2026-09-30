using Stelliberty.Application.Platform;

namespace Stelliberty.Desktop.Services;

internal static class DesktopApplicationLayout
{
    private static string BaseDirectory => AppContext.BaseDirectory;

    private static string InstallRootDirectory
    {
        get
        {
            var baseDirectory = new DirectoryInfo(BaseDirectory);
            // 发布版 UI 位于 data/deps，所有安装资源仍以发布根目录为基准。
            if (baseDirectory.Name == PathConventions.DepsSubdirectory
                && baseDirectory.Parent is { Name: PathConventions.DataDirectoryName, Parent: { } installRoot })
            {
                return installRoot.FullName;
            }

            return BaseDirectory;
        }
    }

    private static string InstallDataDirectory => Path.Combine(InstallRootDirectory, PathConventions.DataDirectoryName);

    // 安装资源随版本替换，macOS/Linux 用户数据固定在用户目录。
    public static string AppDataDirectory => AppDataDirectoryResolver.Resolve(
        InstallRootDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        isPortable: OperatingSystem.IsWindows());

    public static string DepsDirectory => Path.Combine(InstallDataDirectory, PathConventions.DepsSubdirectory);

    // mihomo 工作目录与实际运行的核心，由托盘启动时从安装载体部署。
    public static string CoreDirectory => Path.Combine(AppDataDirectory, PathConventions.CoreSubdirectory);

    public static string CoreBinaryPath => Path.Combine(CoreDirectory, CoreBinaryName);

    public static string RuntimeDirectory => Path.Combine(AppDataDirectory, PathConventions.RuntimeSubdirectory);

    public static string AppLogsDirectory => Path.Combine(AppDataDirectory, PathConventions.AppLogsSubdirectory);

    public static string RunningLogFilePath => Path.Combine(AppLogsDirectory, PathConventions.RunningLogFileName);

    public static string TrayRunningLogFilePath => Path.Combine(AppLogsDirectory, PathConventions.TrayRunningLogFileName);

    public static string SettingsFilePath => Path.Combine(AppDataDirectory, PathConventions.SettingsFileName);

    public static string TrayBinaryPath => Path.Combine(InstallRootDirectory, AppRuntimeNames.TrayBinaryName);

    private static string CoreBinaryName => OperatingSystem.IsWindows() ? "clash-mihomo-core.exe" : "clash-mihomo-core";
}
