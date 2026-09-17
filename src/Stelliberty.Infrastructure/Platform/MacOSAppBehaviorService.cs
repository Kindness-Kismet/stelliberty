using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;

namespace Stelliberty.Infrastructure.Platform;

public sealed class MacOSAppBehaviorService : IAppBehaviorService
{
    private readonly string _binaryPath;

    public MacOSAppBehaviorService(string binaryPath) => _binaryPath = binaryPath;

    public void Apply(AppBehaviorApplicationRequest request)
    {
        ApplyAutoStart(request.IsAutoStartEnabled, request.IsSilentStartEnabled);
        AppLogger.Info($"macOS app behavior applied: autoStart={request.IsAutoStartEnabled}");
    }

    private void ApplyAutoStart(bool isEnabled, bool isSilentStartEnabled)
    {
        var path = LaunchAgentPath();
        if (!isEnabled)
        {
            DeleteIfExists(path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, AutoStartEntryBuilder.MacOSLaunchAgentPlist(_binaryPath, isSilentStartEnabled));
    }

    private static string LaunchAgentPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Path.GetTempPath();
        }

        return Path.Combine(home, "Library", "LaunchAgents", AutoStartEntryBuilder.MacOSLaunchAgentFileName);
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
            AppLogger.Warning($"macOS autostart item delete failed: {exception.Message}");
        }
    }
}
