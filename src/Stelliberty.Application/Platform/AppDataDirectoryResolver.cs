namespace Stelliberty.Application.Platform;

public static class AppDataDirectoryResolver
{
    // Windows 便携安装，数据随安装目录；macOS/Linux 安装载体会被整体替换且可能只读，数据放用户目录。
    // 末级固定为 data：服务端以最近的 data 祖先目录限定核心路径。
    public static string Resolve(string installRootDirectory, string localApplicationDataDirectory, bool isPortable) =>
        isPortable
            ? Path.Combine(installRootDirectory, PathConventions.DataDirectoryName)
            : Path.Combine(
                localApplicationDataDirectory,
                AppRuntimeNames.UserDirectoryName,
                PathConventions.DataDirectoryName);
}
