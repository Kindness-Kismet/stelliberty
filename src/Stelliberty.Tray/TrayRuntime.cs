using Avalonia.Controls.ApplicationLifetimes;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;
using Stelliberty.Infrastructure.Platform;
using Stelliberty.Infrastructure.Proxies;
using Stelliberty.Infrastructure.Tray;
using Stelliberty.Infrastructure.Settings;

namespace Stelliberty.Tray;

internal sealed class TrayRuntime
{
    public async Task<int> RunAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        using var lifetime = new TrayLifetime();
        Console.CancelKeyPress += OnCancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        desktop.Exit += OnDesktopExit;
        void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs args)
        {
            args.Cancel = true;
            lifetime.RequestStop();
        }

        void OnProcessExit(object? sender, EventArgs args) => lifetime.RequestStop();
        void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs args) => lifetime.RequestStop();

        try
        {
            await using var uiLauncher = new DesktopUiLauncher();
            var uiSessions = new UiSessionManager(uiLauncher);
            var coreLogs = new CoreLogJournal();
            await using var coreRuntime = new TrayCoreRuntimeHost(coreLogs);
            await using var backgroundTasks = new TrayBackgroundTasks(coreRuntime);
            await using var runtimeMonitor = new RuntimeTrafficMonitor(
                coreRuntime,
                new PipeCoreProxyClient(TrayCoreEndpoints.Core));
            await using var systemProxy = new LocalSystemProxyController(
                SystemProxyServiceFactory.Create(CurrentSystemProxyPlatform(), TrayApplicationLayout.AppDataDirectory));
            using var sessionEndCleanup = new SessionEndCleanupService(
                () => systemProxy.Shutdown(), coreRuntime.SetShutdownSuspension);
            await using var trayMenu = new TrayMenuService(
                uiSessions,
                coreRuntime,
                runtimeMonitor,
                systemProxy,
                lifetime);
            await trayMenu.StartAsync();
            using var router = new TrayRequestRouter(
                lifetime,
                uiSessions,
                coreRuntime,
                coreLogs,
                runtimeMonitor,
                systemProxy,
                trayMenu,
                backgroundTasks);
            await using var server = new TrayIpcServer(
                TrayEndpoint.Current,
                router.HandleAsync,
                router.OnConnectionClosedAsync);
            sessionEndCleanup.Start();
            runtimeMonitor.Start(lifetime.StoppingToken);
            server.Start(lifetime.StoppingToken);
            var startup = StartCoreAsync(coreRuntime, systemProxy, lifetime.StoppingToken);
            backgroundTasks.Start(startup);
            AppLogger.Info($"Tray startup: pid={Environment.ProcessId} channel={AppRuntimeNames.ChannelName}");
            if (Program.ActivateUiOnStart)
            {
                await uiSessions.ActivateAsync(lifetime.StoppingToken).ConfigureAwait(false);
            }

            await startup.ConfigureAwait(false);
            await server.Completion.ConfigureAwait(false);
            await uiLauncher.DisposeAsync().ConfigureAwait(false);
            AppLogger.Info("Tray shutdown");
            return 0;
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Tray startup failed");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= OnCancelKeyPress;
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            desktop.Exit -= OnDesktopExit;
        }
    }

    private static async Task StartCoreAsync(
        ITrayCoreRuntime coreRuntime,
        ISystemProxyController systemProxy,
        CancellationToken cancellationToken)
    {
        var result = await coreRuntime.EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            AppLogger.Warning($"Background core startup failed: {result.Message}");
            return;
        }

        var settings = new JsonAppSettingsStore(new TrayPlatformDirectories()).Load();
        if (settings.IsLazyModeEnabled)
        {
            var applied = await systemProxy.SetEnabledAsync(true,
                SystemProxyApplicationRequest.Build(settings, CurrentSystemProxyPlatform()),
                cancellationToken).ConfigureAwait(false);
            if (!applied.IsSuccess)
            {
                AppLogger.Warning($"Background system proxy startup failed: {applied.Message}");
            }
        }
    }

    private static SystemProxyPlatform CurrentSystemProxyPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return SystemProxyPlatform.Windows;
        }

        if (OperatingSystem.IsLinux())
        {
            return SystemProxyPlatform.Linux;
        }

        return OperatingSystem.IsMacOS()
            ? SystemProxyPlatform.MacOS
            : SystemProxyPlatform.Other;
    }
}
