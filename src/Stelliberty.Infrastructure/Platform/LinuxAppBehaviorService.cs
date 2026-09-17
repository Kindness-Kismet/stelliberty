using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;

namespace Stelliberty.Infrastructure.Platform;

public sealed class LinuxAppBehaviorService : IAppBehaviorService
{
    private readonly string _binaryPath;

    public LinuxAppBehaviorService(string binaryPath) => _binaryPath = binaryPath;

    public void Apply(AppBehaviorApplicationRequest request)
    {
        ApplyAutoStart(request.IsAutoStartEnabled, request.IsSilentStartEnabled);
        AppLogger.Info($"Linux app behavior applied: autoStart={request.IsAutoStartEnabled}");
    }

    private void ApplyAutoStart(bool isEnabled, bool isSilentStartEnabled)
    {
        var path = AutoStartFilePath();
        if (!isEnabled)
        {
            DeleteIfExists(path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, AutoStartEntryBuilder.LinuxDesktopEntry(_binaryPath, isSilentStartEnabled));
    }

    private static string AutoStartFilePath()
    {
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configHome))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            configHome = string.IsNullOrWhiteSpace(home)
                ? Path.Combine(Path.GetTempPath(), ".config")
                : Path.Combine(home, ".config");
        }

        return Path.Combine(configHome, "autostart", AutoStartEntryBuilder.LinuxDesktopFileName);
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLogger.Warning($"Linux autostart item delete failed: {exception.Message}");
        }
    }
}
