using System.Security.Cryptography;
using System.Text;
using Stelliberty.Application.Platform;

namespace Stelliberty.Infrastructure.Tray;

public static class TrayEndpoint
{
    private static readonly string UserScope = CreateUserScope();

    public static string Current => OperatingSystem.IsWindows()
        ? $"{AppRuntimeNames.FileNameToken}_tray_{ChannelToken}_{UserScope}"
        : Path.Combine(RuntimeDirectory, $"tray-{ChannelToken}.sock");

    public static string LockFilePath => Path.Combine(RuntimeDirectory, $"tray-{ChannelToken}.lock");

    public static void PrepareRuntimeDirectory()
    {
        Directory.CreateDirectory(RuntimeDirectory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                RuntimeDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static string RuntimeDirectory
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (!string.IsNullOrWhiteSpace(configured) && Path.IsPathRooted(configured))
            {
                return Path.Combine(configured, AppRuntimeNames.FileNameToken);
            }

            var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localData, AppRuntimeNames.FileNameToken, "runtime");
        }
    }

    private static string ChannelToken => AppRuntimeNames.ChannelName.ToLowerInvariant();

    private static string CreateUserScope()
    {
        var source = $"{Environment.UserName}|{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(bytes.AsSpan(0, 6)).ToLowerInvariant();
    }
}
