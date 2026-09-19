using Stelliberty.Application.Proxies;
using Stelliberty.Application.Runtime;
using Stelliberty.Domain.Proxies;
using Stelliberty.Infrastructure.Proxies;
using Stelliberty.Infrastructure.Settings;
using Stelliberty.Infrastructure.Subscriptions;
using Stelliberty.Infrastructure.Tray;

namespace Stelliberty.Tray;

internal sealed class TrayProxyCatalog : IProxyDelayResultSink, IDisposable
{
    private readonly ITrayCoreRuntime _coreRuntime;
    private readonly PipeCoreProxyClient _client = new(TrayCoreEndpoints.Core);
    private readonly FileSubscriptionSelectionStore _selection = new(TrayApplicationLayout.AppDataDirectory);
    private readonly JsonAppSettingsStore _settings = new(new TrayPlatformDirectories());
    private readonly ProxyDelayCache _delays = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _stateGate = new();
    private ProxyConfig _config = new([], new Dictionary<string, ProxyNode>());
    private (string? Subscription, string Url, long Generation, bool Running)? _context;
    private bool _isApplyingConfig;
    private DateTimeOffset _lastRefresh;

    public TrayProxyCatalog(ITrayCoreRuntime coreRuntime)
    {
        _coreRuntime = coreRuntime;
        _coreRuntime.StateChanged += OnCoreStateChanged;
        _coreRuntime.ConfigurationChanging += OnConfigurationChanging;
        _coreRuntime.ConfigurationChanged += OnConfigurationChanged;
    }

    public TrayProxySnapshot GetSnapshot()
    {
        lock (_stateGate)
        {
            SynchronizeContext();
            return new TrayProxySnapshot(_delays.Scope, _config.WithEntryDelays(_delays.GetDelays()),
                _context?.Running == true && !_isApplyingConfig);
        }
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

    public async Task RefreshAsync(CancellationToken cancellationToken, bool force = false)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string scope;
            lock (_stateGate)
            {
                SynchronizeContext();
                if (_isApplyingConfig || _context?.Running != true
                    || (!force && DateTimeOffset.UtcNow - _lastRefresh < TimeSpan.FromSeconds(2)))
                {
                    return;
                }
                scope = _delays.Scope;
                _lastRefresh = DateTimeOffset.UtcNow;
            }

            var config = await new MihomoApiProxyConfigProvider(_client)
                .LoadAsync(cancellationToken).ConfigureAwait(false);
            lock (_stateGate)
            {
                SynchronizeContext();
                if (scope == _delays.Scope && !_isApplyingConfig)
                {
                    _config = config;
                }
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public async Task SelectAsync(string scope, string groupName, string nodeName, CancellationToken cancellationToken)
    {
        string? subscription;
        lock (_stateGate)
        {
            SynchronizeContext();
            if (scope != _delays.Scope || _isApplyingConfig || _context?.Running != true)
            {
                throw new InvalidOperationException("Proxy menu is no longer current.");
            }
            subscription = _context.Value.Subscription;
        }

        await _coreRuntime.SelectProxyAsync(subscription, new ProxyChangeRequest(groupName, nodeName), cancellationToken)
            .ConfigureAwait(false);
        await RefreshAsync(cancellationToken, force: true).ConfigureAwait(false);
    }

    private void SynchronizeContext()
    {
        var status = _coreRuntime.CurrentStatus;
        var context = (_selection.GetCurrentSubscriptionId(), _settings.Load().DelayTestUrl,
            status.CoreGeneration, status.Snapshot.State == CoreState.Running);
        if (_context == context) return;
        _context = context;
        Reset();
    }

    private void Reset()
    {
        _delays.Reset();
        _config = new ProxyConfig([], new Dictionary<string, ProxyNode>());
        _lastRefresh = default;
    }

    private void OnConfigurationChanging(object? sender, EventArgs args)
    {
        lock (_stateGate)
        {
            _isApplyingConfig = true;
            Reset();
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
            Reset();
            _isApplyingConfig = false;
        }
    }

    public void Dispose()
    {
        _coreRuntime.StateChanged -= OnCoreStateChanged;
        _coreRuntime.ConfigurationChanging -= OnConfigurationChanging;
        _coreRuntime.ConfigurationChanged -= OnConfigurationChanged;
        _client.Dispose();
    }
}

internal sealed record TrayProxySnapshot(string Scope, ProxyConfig Config, bool IsCoreRunning);
