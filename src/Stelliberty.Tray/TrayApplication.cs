using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Stelliberty.Application.Diagnostics;

namespace Stelliberty.Tray;

internal sealed partial class TrayApplication : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunAsync(desktop, new TrayRuntime());
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task RunAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        TrayRuntime runtime)
    {
        var exitCode = await runtime.RunAsync(desktop).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() => desktop.TryShutdown(exitCode));
    }
}
