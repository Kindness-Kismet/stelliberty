using System.Text;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Desktop.Services;
using Stelliberty.Infrastructure.Diagnostics;
using Stelliberty.Infrastructure.Settings;

namespace Stelliberty.Desktop;

internal static class Program
{
    internal static bool ShowWindowOnStart { get; private set; }
    internal static SingleInstanceService? SingleInstance { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var isSilent = args.Contains("--silent-start", StringComparer.Ordinal);
        using var singleInstance = new SingleInstanceService();
        if (!singleInstance.OwnsInstance)
        {
            // Avalonia 必须使用入口 STA 线程；同步等待仅限第二个进程的激活请求。
            return isSilent ? 0 : SingleInstanceService.ActivateExistingAsync().GetAwaiter().GetResult();
        }

        AppLogger.Configure(new CapturedAppLogger(DesktopApplicationLayout.RunningLogFilePath));
        DependencyDirectoryService.Configure();
        DesktopCoreAssets.Deploy();
        var forceShow = args.Contains("--show-ui", StringComparer.Ordinal);
        ShowWindowOnStart = !isSilent && (forceShow
            || !new JsonAppSettingsStore(new DesktopPlatformDirectories()).Load().IsSilentStartEnabled);
        SingleInstance = singleInstance;
        try
        {
            AppRuntime.Run(args.Where(argument => argument is not "--silent-start" and not "--show-ui").ToArray());
            return 0;
        }
        finally
        {
            SingleInstance = null;
        }
    }
}
