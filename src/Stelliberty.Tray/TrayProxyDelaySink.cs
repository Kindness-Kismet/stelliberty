using Stelliberty.Application.Proxies;
using Stelliberty.Application.Runtime;
using Stelliberty.Infrastructure.Settings;
using Stelliberty.Infrastructure.Subscriptions;
using Stelliberty.Infrastructure.Tray;

namespace Stelliberty.Tray;

internal sealed class TrayProxyDelaySink : IProxyDelayResultSink, IDisposable
{
    private readonly ITrayCoreRuntime _coreRuntime;
    private readonly FileSubscriptionSelectionStore _selection = new(TrayApplicationLayout.AppDataDirectory);
    private readonly JsonAppSettingsStore _settings = new(new TrayPlatformDirectories());
    private readonly ProxyDelayCache _delays = new();
    private readonly object _stateGate = new();
    private (string? Subscription, string Url, long Generation, bool Running)? _context;
    private bool _isApplyingConfig;

    public TrayProxyDelaySink(ITrayCoreRuntime coreRuntime)
    {
        _coreRuntime = coreRuntime;
        _coreRuntime.StateChanged += OnCoreStateChanged;
        _coreRuntime.ConfigurationChanging += OnConfigurationChanging;
        _coreRuntime.ConfigurationChanged += OnConfigurationChanged;
    }

    public Task<string> CaptureScopeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateGate)
        {
            SynchronizeContext();
            return Task.FromResult(_delays.Scope);
        }
    }

    public Task PublishAsync(ProxyDelayPublication publication, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateGate)
        {
            SynchronizeContext();
            if (!_isApplyingConfig && _context?.Running == true)
            {
                _delays.Publish(publication);
            }
        }
        return Task.CompletedTask;
    }

    private void SynchronizeContext()
    {
        var status = _coreRuntime.CurrentStatus;
        var context = (_selection.GetCurrentSubscriptionId(), _settings.Load().DelayTestUrl,
            status.CoreGeneration, status.Snapshot.State == CoreState.Running);
        if (_context == context) return;
        _context = context;
        _delays.Reset();
    }

    private void OnConfigurationChanging(object? sender, EventArgs args)
    {
        lock (_stateGate)
        {
            _isApplyingConfig = true;
            _delays.Reset();
        }
    }

    private void OnCoreStateChanged(object? sender, TrayCoreStatus status)
    {
        lock (_stateGate) SynchronizeContext();
    }

    private void OnConfigurationChanged(object? sender, EventArgs args)
    {
        lock (_stateGate)
        {
            // 配置应用期间启动的测速也不能写进新的配置会话。
            _delays.Reset();
            _isApplyingConfig = false;
        }
    }

    public void Dispose()
    {
        _coreRuntime.StateChanged -= OnCoreStateChanged;
        _coreRuntime.ConfigurationChanging -= OnConfigurationChanging;
        _coreRuntime.ConfigurationChanged -= OnConfigurationChanged;
    }
}
