using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Runtime;
using Stelliberty.Desktop.Services;
using Stelliberty.Infrastructure.Platform;
using Stelliberty.Infrastructure.Settings;

namespace Stelliberty.Desktop.Runtime;

internal sealed class DesktopRuntime : IAsyncDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly SystemPowerRecoveryService _powerRecovery;
    private readonly SystemPowerMonitor _powerMonitor;
    private readonly SessionEndCleanupService _sessionEndCleanup;
    private Task _powerStartup = Task.CompletedTask;
    private bool _isDisposed;
    private volatile bool _isSystemShutdownRequested;

    public DesktopRuntime(SystemProxyPlatform platform)
    {
        Core = new CoreRuntimeHost();
        SystemProxy = new LocalSystemProxyController(
            SystemProxyServiceFactory.Create(platform, DesktopApplicationLayout.AppDataDirectory));
        Traffic = new RuntimeTrafficMonitor(Core);
        BackgroundTasks = new DesktopBackgroundTasks(Core);
        _powerRecovery = new SystemPowerRecoveryService(new DesktopPowerRecoveryRuntime(Core, SystemProxy, platform));
        _powerMonitor = new SystemPowerMonitor(_powerRecovery);
        _sessionEndCleanup = new SessionEndCleanupService(HandleSessionEnd, SetSystemShutdownRequested);
    }

    public Task<CoreOperationResult> Startup { get; private set; } = null!;
    public CancellationToken StoppingToken => _stopping.Token;

    public CoreRuntimeHost Core { get; }
    public LocalSystemProxyController SystemProxy { get; }
    public RuntimeTrafficMonitor Traffic { get; }
    public DesktopBackgroundTasks BackgroundTasks { get; }
    public SystemPowerRecoverySnapshot PowerRecoverySnapshot => _powerRecovery.Snapshot;
    public string PowerMonitorStatus => _powerMonitor.Status;
    public bool IsSystemShutdownRequested => _isSystemShutdownRequested;

    public Task StartAsync(SystemProxyPlatform platform)
    {
        _sessionEndCleanup.Start();
        Traffic.Start(_stopping.Token);
        Startup = StartCoreAsync(platform);
        BackgroundTasks.Start(Startup);
        _powerStartup = _powerMonitor.StartAsync();
        return _powerStartup;
    }

    private async Task<CoreOperationResult> StartCoreAsync(SystemProxyPlatform platform)
    {
        var result = await Core.EnsureStartedAsync(_stopping.Token).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            AppLogger.Warning($"Core startup failed: {result.Message}");
            return result;
        }

        var settings = new JsonAppSettingsStore(new DesktopPlatformDirectories()).Load();
        if (settings.IsLazyModeEnabled)
        {
            var applied = await SystemProxy.SetEnabledAsync(true,
                SystemProxyApplicationRequest.Build(settings, platform), _stopping.Token).ConfigureAwait(false);
            if (!applied.IsSuccess) AppLogger.Warning($"System proxy startup failed: {applied.Message}");
        }
        return result;
    }

    public void RequestStop() => _stopping.Cancel();

    public void HandleSessionEnd()
    {
        if (_isDisposed) return;
        SetSystemShutdownRequested(true);
        SystemProxy.Shutdown();
    }

    private void SetSystemShutdownRequested(bool isRequested)
    {
        // 系统注销通知可取消，必须同步复位核心状态抑制与退出放行标志。
        _isSystemShutdownRequested = isRequested;
        Core.SetShutdownSuspension(isRequested);
    }

#if DEBUG
    public Task InjectPowerEventAsync(SystemPowerEventKind kind) => _powerRecovery.HandleAsync(kind, "debug");
#endif

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _stopping.Cancel();
        try
        {
            _sessionEndCleanup.Dispose();
            await _powerStartup.ConfigureAwait(false);
            await _powerMonitor.DisposeAsync().ConfigureAwait(false);
            await _powerRecovery.DisposeAsync().ConfigureAwait(false);
            await BackgroundTasks.DisposeAsync().ConfigureAwait(false);
            await Traffic.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await Core.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await SystemProxy.DisposeAsync().ConfigureAwait(false);
                _stopping.Dispose();
            }
        }
    }
}
