using System.Text;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Desktop.Services;
using Stelliberty.Infrastructure.Diagnostics;

namespace Stelliberty.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var launch = DesktopLaunchArguments.Parse(args);
        if (launch is null)
        {
            return 0;
        }

        using var traySession = new DesktopTraySession();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            // Avalonia 必须继续使用入口 STA 线程，因此只在进程边界同步等待托盘授权。
            traySession.RegisterAsync(launch.TraySessionToken, timeout.Token).GetAwaiter().GetResult();
            if (!traySession.CanExitToBackground)
            {
                return 1;
            }
        }
        catch
        {
            return 1;
        }

        ConfigureUiProcess();
        DesktopLaunchContext.TraySession = traySession;
        try
        {
            AppRuntime.RunUi(launch.AvaloniaArguments);
        }
        finally
        {
            DesktopLaunchContext.TraySession = null;
        }

        return 0;
    }

    private static void ConfigureUiProcess()
    {
        AppLogger.Configure(new CapturedAppLogger(DesktopApplicationLayout.RunningLogFilePath));
        DependencyDirectoryService.Configure();
    }
}
