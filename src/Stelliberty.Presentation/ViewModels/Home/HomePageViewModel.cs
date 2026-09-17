using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Localization;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Proxies;
using Stelliberty.Domain.Proxies;
using Stelliberty.Application.Runtime;
using Stelliberty.Application.Updates;
using Stelliberty.Presentation.Commands;
using Stelliberty.Presentation.Formatting;

namespace Stelliberty.Presentation.ViewModels;

public sealed class HomePageViewModel : ViewModelBase, IDisposable
{
    private readonly ILocalizationService? _localization;
    private readonly SystemProxyPlatform _systemPlatform;
    private readonly IClipboardWriter? _clipboardWriter;
    private readonly ISystemProxyController _systemProxyService;
    private readonly IServiceModeManager? _serviceModeManager;
    private readonly Func<SystemProxyApplicationRequest> _systemProxyRequestFactory;
    private readonly Action<bool>? _tunStateChanged;

    private readonly Func<Task>? _coreRestart;
    private readonly ICoreUpdater? _coreUpdater;
    private readonly INetworkConnectionProbe? _networkProbe;

    private readonly ProcessRunMode _runMode;
    private readonly IProxyCoreClient? _proxyClient;
    private readonly Func<string>? _proxyEndpointProvider;
    private readonly Action<string>? _coreVersionChanged;
    private readonly Func<DateTimeOffset> _now;

    private readonly SynchronizationContext? _uiContext;
    private bool _isSystemProxyEnabled;
    private int _systemProxyApplyVersion;
    private int _pendingSystemProxyOperations;
    private bool _isTunEnabled;
    private int _tunApplyVersion;

    private bool _isTakeoverTunTab;
    private bool _isCoreRunning;
    private OutboundMode _outboundMode = OutboundMode.Rule;
    private DateTimeOffset? _coreRunningSince;
    private TimeSpan _uptime = TimeSpan.Zero;
    private string? _memoryValueText;
    private readonly string _platformValueText;
    private string? _coreVersionValueText;
    private bool _shouldRefreshCoreVersion = true;
    private string? _proxyAddressValueText;
    private long _uploadSpeed;
    private long _downloadSpeed;
    private long _uploadTotal;
    private long _downloadTotal;
    private int _activeConnectionCount;

    // 30 个采样间隔构成 30 秒可见窗口，故需 31 个点
    private const int SpeedHistoryCapacity = 31;
    private readonly Queue<double> _uploadSpeedHistory = new();
    private readonly Queue<double> _downloadSpeedHistory = new();
    private bool _isSpeedChartLive = true;
    private readonly TrafficRateTracker _directRuntimeTrafficTracker = new();

    private bool _isCoreRestarting;
    private bool _isCoreUpdating;
    private bool _isServiceModeBusy;
    private bool _isRefreshingServiceMode;
    private long _runtimeRefreshVersion;
    private bool _isDisposed;
    private ServiceModeStatus _serviceModeStatus = ServiceModeStatus.Unavailable(string.Empty);
    private DateTimeOffset? _lastServiceModeProbe;
    private long _serviceModeRefreshVersion;
    private NetworkConnectionType _networkType = NetworkConnectionType.Disconnected;
    private string _networkName = string.Empty;
    private CancellationTokenSource? _refreshCancellation;

    public HomePageViewModel(
        ISystemProxyController systemProxyService,
        Func<SystemProxyApplicationRequest> systemProxyRequestFactory,
        IServiceModeManager? serviceModeManager = null,
        Action<bool>? tunStateChanged = null,
        INetworkConnectionProbe? networkProbe = null,
        IProxyCoreClient? proxyClient = null,
        Func<string>? proxyEndpointProvider = null,
        string? cachedCoreVersion = null,
        Action<string>? coreVersionChanged = null,
        Func<DateTimeOffset>? now = null,
        Func<Task>? coreRestart = null,
        ICoreUpdater? coreUpdater = null,
        IProcessPrivilegeProbe? privilegeProbe = null,
        ServiceModeStatus? initialServiceModeStatus = null,
        ILocalizationService? localization = null,
        SystemProxyPlatform systemPlatform = SystemProxyPlatform.Other,
        IClipboardWriter? clipboardWriter = null)
    {
        _localization = localization;
        _systemPlatform = systemPlatform;
        _clipboardWriter = clipboardWriter;
        _systemProxyService = systemProxyService;
        _systemProxyService.StatusChanged += OnSystemProxyStatusChanged;
        _serviceModeManager = serviceModeManager;
        _systemProxyRequestFactory = systemProxyRequestFactory;
        _tunStateChanged = tunStateChanged;
        _coreRestart = coreRestart;
        _coreUpdater = coreUpdater;
        _networkProbe = networkProbe;
        _runMode = privilegeProbe?.Detect() ?? ProcessRunMode.Normal;
        _serviceModeStatus = initialServiceModeStatus ?? _serviceModeStatus;
        _proxyClient = proxyClient;
        _proxyEndpointProvider = proxyEndpointProvider;
        _coreVersionValueText = string.IsNullOrWhiteSpace(cachedCoreVersion) ? null : cachedCoreVersion;
        _coreVersionChanged = coreVersionChanged;
        _now = now ?? (() => DateTimeOffset.Now);
        _uiContext = SynchronizationContext.Current;
        _refreshCancellation = new CancellationTokenSource();
        _platformValueText = $"{PlatformName()} {RuntimeInformation.OSArchitecture}";
        _proxyAddressValueText = proxyEndpointProvider?.Invoke();
        if (_localization is not null)
        {
            _localization.LanguageChanged += OnLanguageChanged;
        }
        ToggleSystemProxyCommand = new RelayCommand(ToggleSystemProxy);
        SetRuleOutboundCommand = new RelayCommand(() => _ = SetOutboundModeAsync(OutboundMode.Rule));
        SetGlobalOutboundCommand = new RelayCommand(() => _ = SetOutboundModeAsync(OutboundMode.Global));
        SetDirectOutboundCommand = new RelayCommand(() => _ = SetOutboundModeAsync(OutboundMode.Direct));
        SelectTakeoverProxyTabCommand = new RelayCommand(() => SetTakeoverTab(false));
        SelectTakeoverTunTabCommand = new RelayCommand(() => SetTakeoverTab(true));
        ResetTrafficCommand = new RelayCommand(ResetTraffic);
        RefreshCoreCommand = new RelayCommand(() => _ = UpdateCoreAsync());
        RestartCoreCommand = new RelayCommand(() => _ = RestartCoreAsync());
        ToggleServiceModeCommand = new RelayCommand(() => _ = ToggleServiceModeAsync());

        SeedZeroHistory();
        _ = RefreshSystemProxyStatusAsync();
        RefreshServiceMode(force: true);
    }

    public event EventHandler<(string Message, ToastType Type)>? ToastRequested;

    public bool IsSystemProxyEnabled
    {
        get => _isSystemProxyEnabled;
        set
        {
            if (_isSystemProxyEnabled == value)
            {
                return;
            }

            SetSystemProxy(value);
        }
    }

    public void ApplyTunState(bool isEnabled)
    {
        if (_isTunEnabled == isEnabled)
        {
            return;
        }

        Interlocked.Increment(ref _tunApplyVersion);
        _isTunEnabled = isEnabled;
        RaiseHomeStateChanged();
    }

    public void ApplyOutboundMode(OutboundMode mode)
    {
        if (_outboundMode == mode)
        {
            return;
        }

        _outboundMode = mode;
        RaiseHomeStateChanged();
    }

    public void ApplyCoreRunning(bool isRunning)
    {
        var now = _now();
        if (isRunning && _coreRunningSince is null)
        {
            _coreRunningSince = now;
        }

        if (isRunning && _coreRunningSince is { } since)
        {
            _uptime = now >= since ? now - since : TimeSpan.Zero;
        }
        else if (!isRunning && _isCoreRunning)
        {
            // 仅在运行转为停止时清空；初始与重复的停止状态不能打断图表采样。
            _coreRunningSince = null;
            _uptime = TimeSpan.Zero;

            _directRuntimeTrafficTracker.Reset();
            _uploadSpeed = 0;
            _downloadSpeed = 0;
            _uploadTotal = 0;
            _downloadTotal = 0;
            _memoryValueText = null;
            _activeConnectionCount = 0;
            SeedZeroHistory();
        }

        _isCoreRunning = isRunning;
        RaiseHomeStateChanged();
    }

    public bool IsTunEnabled
    {
        get => _isTunEnabled;
        set
        {
            if (_isTunEnabled == value)
            {
                return;
            }

            // 没有权限或核心不稳定时，开关会回弹。
            if ((value && !CanToggleTun) || !IsCoreInteractive)
            {
                OnPropertyChanged(nameof(IsTunEnabled));
                return;
            }

            SetTun(value);
        }
    }

    public bool CanToggleTun => _runMode is ProcessRunMode.Administrator or ProcessRunMode.Service
        || (_serviceModeStatus.IsRunning && IsServiceModeCoreHostActive);

    public string CoreHostMode => IsServiceModeCoreHostActive ? "service" : "process";

    public string PrivilegeModeText => _serviceModeStatus.IsRunning
        ? Localize("Home.RunMode.Service")
        : _runMode switch
        {
            ProcessRunMode.Administrator => Localize("Home.RunMode.Administrator"),
            ProcessRunMode.Service => Localize("Home.RunMode.Service"),
            _ => Localize("Home.RunMode.Normal")
        };

    public ServiceModeState ServiceModeState => _serviceModeStatus.State;

    public string ServiceModeMessage => _serviceModeStatus.Message;

    public string ServiceModeButtonText => _serviceModeStatus.NeedsRepair
        ? Localize("Home.ServiceMode.Repair")
        : IsServiceModeUpdateAvailable
            ? Localize("Home.ServiceMode.Update")
            : _serviceModeStatus.IsInstalled
                ? Localize("Home.ServiceMode.Uninstall")
                : Localize("Home.ServiceMode.Install");

    public bool IsServiceModeUpdateAvailable => _serviceModeStatus.IsInstalled
        && !string.IsNullOrWhiteSpace(_serviceModeStatus.InstalledVersion)
        && !string.IsNullOrWhiteSpace(_serviceModeStatus.AvailableVersion)
        && AppVersionComparer.IsValid(_serviceModeStatus.InstalledVersion)
        && AppVersionComparer.IsValid(_serviceModeStatus.AvailableVersion)
        && AppVersionComparer.IsNewer(_serviceModeStatus.AvailableVersion, _serviceModeStatus.InstalledVersion);

    public bool CanToggleServiceMode => !_isServiceModeBusy && _serviceModeManager is not null;

    public bool IsTakeoverProxyTabSelected => !_isTakeoverTunTab;

    public bool IsTakeoverTunTabSelected => _isTakeoverTunTab;

    public bool IsCoreRunning => _isCoreRunning;

    public string CoreStatusValueText => Localize(_isCoreRestarting
        ? "Home.CoreStatus.Restarting"
        : _isCoreRunning
            ? "Home.CoreStatus.Running"
            : "Home.CoreStatus.Stopped");

    public string CoreSignalTag => _isCoreRunning ? string.Empty : "danger";

    public bool IsNetworkConnected => _networkType != NetworkConnectionType.Disconnected;

    public bool IsWifiConnection => _networkType == NetworkConnectionType.Wifi;

    public bool IsWiredConnection => _networkType is NetworkConnectionType.Wired or NetworkConnectionType.Other;

    public string NetworkSignalTag => IsNetworkConnected ? string.Empty : "danger";

    public string NetworkTypeText => _networkType switch
    {
        NetworkConnectionType.Wifi => "Wi-Fi",
        NetworkConnectionType.Wired => Localize("Home.Network.Wired"),
        NetworkConnectionType.Other => Localize("Home.Network.Other"),
        _ => Localize("Home.Network.Disconnected")
    };

    public string NetworkNameValueText => IsNetworkConnected
        ? (string.IsNullOrWhiteSpace(_networkName) ? Localize("Home.Network.Unknown") : _networkName)
        : Localize("Home.Network.Disconnected");

    public OutboundMode OutboundMode => _outboundMode;

    public bool IsRuleOutboundSelected => _outboundMode == OutboundMode.Rule;

    public bool IsGlobalOutboundSelected => _outboundMode == OutboundMode.Global;

    public bool IsDirectOutboundSelected => _outboundMode == OutboundMode.Direct;

    public string OutboundModeDescriptionText => _outboundMode switch
    {
        OutboundMode.Global => Localize("Home.Outbound.Description.Global"),
        OutboundMode.Direct => Localize("Home.Outbound.Description.Direct"),
        _ => Localize("Home.Outbound.Description.Rule")
    };

    public string UptimeValueText => _isCoreRunning && _coreRunningSince is not null
        ? FormatUptime(_uptime < TimeSpan.Zero ? TimeSpan.Zero : _uptime)
        : "—";

    public string MemoryValueText => _memoryValueText ?? Localize("Home.Value.Unavailable");

    public string PlatformValueText => _platformValueText;

    public string CoreVersionValueText => _coreVersionValueText ?? Localize("Home.Value.Unavailable");

    public string ProxyAddressValueText => _proxyAddressValueText ?? Localize("Home.Value.Unavailable");

    public string UploadSpeedValueText => $"{ByteSize.Format(_uploadSpeed)}/s";

    public string DownloadSpeedValueText => $"{ByteSize.Format(_downloadSpeed)}/s";

    public string UploadTotalValueText => ByteSize.Format(_uploadTotal);

    public string DownloadTotalValueText => ByteSize.Format(_downloadTotal);

    public string ActiveConnectionsValueText => _activeConnectionCount.ToString();

    public IReadOnlyList<double> UploadSamples { get; private set; } = Array.Empty<double>();

    public IReadOnlyList<double> DownloadSamples { get; private set; } = Array.Empty<double>();

    // 曲线离开视野（窗口隐藏、切页）时冻结采样，回来后从离开位置接着走。
    public void SetSpeedChartLive(bool isLive) => _isSpeedChartLive = isLive;

    public bool IsCoreRestarting => _isCoreRestarting;

    public bool IsCoreUpdating => _isCoreUpdating;

    public bool IsServiceModeBusy => _isServiceModeBusy;

    public bool CanRestartCore => _isCoreRunning && !_isCoreRestarting && !_isCoreUpdating;

    public bool CanUpdateCore => !_isCoreUpdating && !_isCoreRestarting;

    // 核心仅在运行且未重启/更新时可用。
    public bool IsCoreInteractive => _isCoreRunning && !_isCoreRestarting && !_isCoreUpdating;

    // TUN 只能在有权限且核心稳定时切换。
    public bool IsTunToggleEnabled => CanToggleTun && IsCoreInteractive;

    public ICommand ToggleSystemProxyCommand { get; }

    public ICommand SetRuleOutboundCommand { get; }

    public ICommand SetGlobalOutboundCommand { get; }

    public ICommand SetDirectOutboundCommand { get; }

    public ICommand SelectTakeoverProxyTabCommand { get; }

    public ICommand SelectTakeoverTunTabCommand { get; }

    public ICommand ResetTrafficCommand { get; }

    public ICommand RefreshCoreCommand { get; }

    public ICommand RestartCoreCommand { get; }

    public ICommand ToggleServiceModeCommand { get; }

    public void ApplyNetworkConnection(NetworkConnectionInfo info)
    {
        // Post 投递后可能已销毁，销毁后不得再写状态。
        if (_isDisposed)
        {
            return;
        }

        _networkType = info.Type;
        _networkName = info.Name;
        RaiseHomeStateChanged();
    }

    public void RefreshNetworkConnection()
    {
        if (_networkProbe is null || _refreshCancellation is null)
        {
            return;
        }

        // 探测在后台运行，并把结果发回 UI 线程。
        var cancellationToken = _refreshCancellation.Token;
        _ = Task.Run(() =>
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                var info = _networkProbe.Detect();
                if (!cancellationToken.IsCancellationRequested)
                {
                    Post(() => ApplyNetworkConnection(info));
                }
            }
            catch (Exception exception)
            {
                AppLogger.Warning($"Network connection probe failed: {exception.Message}");
            }
        }, cancellationToken);
    }

    public void RefreshRuntime()
    {
        if (_proxyClient is null || _refreshCancellation is null)
        {
            return;
        }

        var cancellationToken = _refreshCancellation.Token;
        var snapshotLoader = new CoreRuntimeSnapshotLoader(_proxyClient);
        // 快照耗时常超过轮询周期，只应用最后发起的一次，避免总量倒退。
        var refreshVersion = Interlocked.Increment(ref _runtimeRefreshVersion);
        _ = Task.Run(async () =>
        {
            try
            {
                var snapshot = await snapshotLoader.LoadAsync(includeVersion: _shouldRefreshCoreVersion, cancellationToken);
                if (snapshot is not null
                    && !cancellationToken.IsCancellationRequested
                    && refreshVersion == Volatile.Read(ref _runtimeRefreshVersion))
                {
                    Post(() => ApplyRuntime(
                        snapshot.Stats,
                        snapshot.Mode,
                        snapshot.Version,
                        snapshot.ConnectionCount,
                        snapshot.History));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                // 单次刷新失败不能覆盖当前运行状态。
                AppLogger.Warning($"Runtime refresh failed: {exception.Message}");
            }
        }, cancellationToken);
    }

    public void RefreshServiceMode(bool force = false)
    {
        if (_serviceModeManager is null || _refreshCancellation is null || _isRefreshingServiceMode || _isServiceModeBusy)
        {
            return;
        }

        var now = _now();
        if (!force && _lastServiceModeProbe is { } last && now - last < TimeSpan.FromSeconds(10))
        {
            return;
        }

        _lastServiceModeProbe = now;
        _isRefreshingServiceMode = true;
        var cancellationToken = _refreshCancellation.Token;
        var refreshVersion = Interlocked.Increment(ref _serviceModeRefreshVersion);
        _ = Task.Run(async () =>
        {
            try
            {
                await RefreshServiceModeAsync(refreshVersion, cancellationToken);
            }
            catch (Exception exception)
            {
                AppLogger.Warning($"Service mode refresh failed: {exception.Message}");
                if (!cancellationToken.IsCancellationRequested
                    && refreshVersion == Volatile.Read(ref _serviceModeRefreshVersion))
                {
                    Post(() => ApplyServiceModeStatus(ServiceModeStatus.Unavailable(string.Empty)));
                }
            }
            finally
            {
                _isRefreshingServiceMode = false;
            }
        }, cancellationToken);
    }

    public Task<ServiceModeStatus> RefreshServiceModeAsync(CancellationToken cancellationToken = default)
    {
        var refreshVersion = Interlocked.Increment(ref _serviceModeRefreshVersion);
        return RefreshServiceModeAsync(refreshVersion, cancellationToken);
    }

    private async Task<ServiceModeStatus> RefreshServiceModeAsync(long refreshVersion, CancellationToken cancellationToken)
    {
        if (_serviceModeManager is null)
        {
            return ServiceModeStatus.Unavailable(string.Empty);
        }

        var status = await _serviceModeManager.GetStatusAsync(cancellationToken);
        if (!cancellationToken.IsCancellationRequested
            && refreshVersion == Volatile.Read(ref _serviceModeRefreshVersion))
        {
            await ApplyServiceModeStatusAsync(status);
        }
        return status;
    }

    private void ApplyRuntime(
        CoreRuntimeStats? stats,
        OutboundMode? mode,
        string? version,
        int? connectionCount,
        IReadOnlyList<CoreRuntimeSample> history)
    {
        // 取消检查与 Post 投递之间存在窗口，销毁后不得再写状态。
        if (_isDisposed)
        {
            return;
        }

        if (stats is not null)
        {
            var directSample = _directRuntimeTrafficTracker.Update(
                stats.UploadTotal,
                stats.DownloadTotal,
                _now());
            _uploadSpeed = stats.HasTrafficRate ? stats.UploadSpeed : directSample.UploadSpeed;
            _downloadSpeed = stats.HasTrafficRate ? stats.DownloadSpeed : directSample.DownloadSpeed;
            _uploadTotal = history.Count > 0 ? stats.UploadTotal : directSample.UploadTotal;
            _downloadTotal = history.Count > 0 ? stats.DownloadTotal : directSample.DownloadTotal;
            _memoryValueText = ByteSize.Format(stats.Memory);
            if (_isSpeedChartLive)
            {
                if (history.Count > 0)
                {
                    ApplySpeedHistory(history);
                }
                else
                {
                    PushSpeedSample(_uploadSpeedHistory, _uploadSpeed);
                    PushSpeedSample(_downloadSpeedHistory, _downloadSpeed);
                }
                UploadSamples = _uploadSpeedHistory.ToArray();
                DownloadSamples = _downloadSpeedHistory.ToArray();
            }
        }

        if (mode is { } coreMode)
        {
            _outboundMode = coreMode;
        }

        if (!string.IsNullOrWhiteSpace(version))
        {
            _shouldRefreshCoreVersion = false;
            if (!string.Equals(_coreVersionValueText, version, StringComparison.Ordinal))
            {
                _coreVersionValueText = version;
                _coreVersionChanged?.Invoke(version);
            }
        }

        if (connectionCount is { } count)
        {
            _activeConnectionCount = count;
        }

        _proxyAddressValueText = _proxyEndpointProvider?.Invoke() ?? _proxyAddressValueText;
        _uptime = _isCoreRunning && _coreRunningSince is { } since ? _now() - since : TimeSpan.Zero;
        RaiseHomeStateChanged();
    }

    private void ToggleSystemProxy()
    {
        SetSystemProxy(!_isSystemProxyEnabled);
    }

    public void ToggleSystemProxyFromHotkey()
    {
        if (!IsSystemProxyEnabled && !IsCoreInteractive)
        {
            RaiseToast(Localize("Home.Toast.SystemProxyHotkeyUnavailable"), ToastType.Warning);
            return;
        }

        ToggleSystemProxy();
    }

    public void ToggleTunFromHotkey()
    {
        if (!IsCoreInteractive || (!IsTunEnabled && !CanToggleTun))
        {
            RaiseToast(Localize("Home.Toast.TunHotkeyUnavailable"), ToastType.Warning);
            return;
        }

        IsTunEnabled = !IsTunEnabled;
    }

    private void SetTun(bool shouldEnable)
    {
        var version = Interlocked.Increment(ref _tunApplyVersion);
        _isTunEnabled = shouldEnable;
        RaiseHomeStateChanged();
        _ = ApplyTunAsync(shouldEnable, version);
    }

    private async Task ApplyTunAsync(bool shouldEnable, int version)
    {
        try
        {
            await Task.Yield();
            if (_isDisposed || version != Volatile.Read(ref _tunApplyVersion))
            {
                return;
            }

            _tunStateChanged?.Invoke(shouldEnable);
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"TUN apply failed: {exception.Message}");
            if (!_isDisposed)
            {
                Post(() => ApplyTunFailure(shouldEnable, version));
            }
        }
    }

    private void ApplyTunFailure(bool attemptedState, int version)
    {
        if (_isDisposed || version != Volatile.Read(ref _tunApplyVersion))
        {
            return;
        }

        _isTunEnabled = !attemptedState;
        RaiseHomeStateChanged();
        RaiseToast(Localize("Home.Toast.TunApplyFailed"), ToastType.Error);
    }

    public void ReapplySystemProxySettings()
    {
        if (!_isSystemProxyEnabled)
        {
            return;
        }

        SetSystemProxy(true);
    }

    private void SetSystemProxy(bool shouldEnable)
    {
        var version = Interlocked.Increment(ref _systemProxyApplyVersion);
        _isSystemProxyEnabled = shouldEnable;
        RaiseHomeStateChanged();
        _ = Task.Run(() => ApplySystemProxyAsync(shouldEnable, version));
    }

    private async Task ApplySystemProxyAsync(bool shouldEnable, int version)
    {
        Interlocked.Increment(ref _pendingSystemProxyOperations);
        try
        {
            var request = shouldEnable ? _systemProxyRequestFactory.Invoke() : null;
            var result = await _systemProxyService.SetEnabledAsync(
                shouldEnable,
                request,
                _refreshCancellation?.Token ?? CancellationToken.None).ConfigureAwait(false);
            if (_isDisposed || version != Volatile.Read(ref _systemProxyApplyVersion))
            {
                return;
            }

            Post(() => ApplySystemProxyResult(result, version));
            if (!result.IsSuccess)
            {
                AppLogger.Warning($"System proxy apply returned failure: {result.Message}");
            }
        }
        catch (OperationCanceledException) when (_isDisposed)
        {
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"System proxy apply failed: {exception.Message}");
            if (!_isDisposed)
            {
                Post(() => ApplySystemProxyFailure(shouldEnable, version));
            }
        }
        finally
        {
            Interlocked.Decrement(ref _pendingSystemProxyOperations);
        }
    }

    private void ApplySystemProxyFailure(bool attemptedState, int version)
    {
        if (_isDisposed || version != Volatile.Read(ref _systemProxyApplyVersion))
        {
            return;
        }

        _isSystemProxyEnabled = !attemptedState;
        RaiseHomeStateChanged();

        RaiseToast(Localize("Home.Toast.SystemProxyFailed"), ToastType.Error);
    }

    private void ApplySystemProxyResult(SystemProxyApplyResult result, int version)
    {
        if (_isDisposed || version != Volatile.Read(ref _systemProxyApplyVersion))
        {
            return;
        }

        _isSystemProxyEnabled = result.Status.IsEnabled;
        RaiseHomeStateChanged();
        if (!result.IsSuccess)
        {
            RaiseToast(Localize("Home.Toast.SystemProxyFailed"), ToastType.Error);
        }
    }

    private async Task RefreshSystemProxyStatusAsync()
    {
        var version = Volatile.Read(ref _systemProxyApplyVersion);
        try
        {
            var status = await _systemProxyService.GetStatusAsync(
                _refreshCancellation?.Token ?? CancellationToken.None).ConfigureAwait(false);
            if (!_isDisposed && version == Volatile.Read(ref _systemProxyApplyVersion))
            {
                Post(() =>
                {
                    if (version == Volatile.Read(ref _systemProxyApplyVersion))
                    {
                        ApplySystemProxyStatus(status);
                    }
                });
            }
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
            AppLogger.Warning($"System proxy status refresh failed: {exception.Message}");
        }
    }

    private void OnSystemProxyStatusChanged(object? sender, SystemProxyStatus status)
    {
        if (_isDisposed || Volatile.Read(ref _pendingSystemProxyOperations) != 0)
        {
            return;
        }

        Post(() => ApplySystemProxyStatus(status));
    }

    private void ApplySystemProxyStatus(SystemProxyStatus status)
    {
        if (_isDisposed)
        {
            return;
        }

        _isSystemProxyEnabled = status.IsEnabled;
        RaiseHomeStateChanged();
    }

    private async Task RestartCoreAsync()
    {
        if (_isCoreRestarting || _coreRestart is null)
        {
            return;
        }

        _isCoreRestarting = true;
        RaiseHomeStateChanged();
        try
        {
            await _coreRestart();
            ReapplySystemProxySettings();
            InvalidateCoreVersion();
            RaiseToast(Localize("Home.Toast.CoreRestarted"), ToastType.Success);
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Core restart failed");
            RaiseToast(Localize("Home.Toast.CoreRestartFailed"), ToastType.Error);
        }
        finally
        {
            _isCoreRestarting = false;
            Post(RaiseHomeStateChanged);
        }
    }

    private async Task UpdateCoreAsync()
    {
        if (_isCoreUpdating || _coreUpdater is null)
        {
            return;
        }

        _isCoreUpdating = true;
        RaiseHomeStateChanged();
        RaiseToast(Localize("Home.Toast.CoreUpdateChecking"), ToastType.Info);
        try
        {
            var result = await _coreUpdater.UpdateAsync(_refreshCancellation?.Token ?? CancellationToken.None);
            switch (result.Status)
            {
                case CoreUpdateStatus.Updated:
                    InvalidateCoreVersion();
                    RaiseToast(string.Format(Localize("Home.Toast.CoreUpdated"), result.Version ?? string.Empty), ToastType.Success);
                    break;
                case CoreUpdateStatus.UpToDate:
                    RaiseToast(Localize("Home.Toast.CoreUpToDate"), ToastType.Success);
                    break;
                default:
                    AppLogger.Warning($"Core update returned failure: {result.Message}");
                    RaiseToast(Localize("Home.Toast.CoreUpdateFailed"), ToastType.Error);
                    break;
            }
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Core update failed");
            RaiseToast(Localize("Home.Toast.CoreUpdateFailed"), ToastType.Error);
        }
        finally
        {
            _isCoreUpdating = false;
            Post(RaiseHomeStateChanged);
        }
    }

    private async Task ToggleServiceModeAsync()
    {
        var installOrUpdate = !_serviceModeStatus.NeedsRepair
            && (!_serviceModeStatus.IsInstalled || IsServiceModeUpdateAvailable);
        await ExecuteServiceModeOperationAsync(installOrUpdate);
    }

    public Task<ServiceModeOperationResult> InstallOrUpdateServiceModeAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteServiceModeOperationAsync(installOrUpdate: true, cancellationToken);
    }

    public Task<ServiceModeOperationResult> UninstallServiceModeAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteServiceModeOperationAsync(installOrUpdate: false, cancellationToken);
    }

    private async Task<ServiceModeOperationResult> ExecuteServiceModeOperationAsync(bool installOrUpdate, CancellationToken cancellationToken = default)
    {
        if (_isServiceModeBusy || _serviceModeManager is null)
        {
            return ServiceModeOperationResult.Failed("Service mode is not available yet.");
        }

        _isServiceModeBusy = true;
        RaiseHomeStateChanged();
        var token = cancellationToken.CanBeCanceled ? cancellationToken : _refreshCancellation?.Token ?? CancellationToken.None;
        ServiceModeOperationResult result;
        try
        {
            result = installOrUpdate
                ? await _serviceModeManager.InstallOrUpdateAsync(token)
                : await _serviceModeManager.UninstallAsync(token);

            if (result.IsCanceled)
            {
                RaiseToast(Localize("Home.Toast.ServiceModeOperationCanceled"), ToastType.Info);
            }
            else if (result.IsSuccess)
            {
                var key = installOrUpdate
                    ? "Home.Toast.ServiceModeInstallSucceeded"
                    : "Home.Toast.ServiceModeUninstallSucceeded";
                RaiseToast(Localize(key), ToastType.Success);
            }
            else
            {
                AppLogger.Warning($"Service mode operation returned failure: {result.Message}");
                RaiseToast(Localize(installOrUpdate
                    ? "Home.Toast.ServiceModeInstallFailed"
                    : "Home.Toast.ServiceModeUninstallFailed"), ToastType.Error);
            }
        }
        catch (OperationCanceledException exception) when (token.IsCancellationRequested)
        {
            result = ServiceModeOperationResult.Canceled(exception.Message);
            RaiseToast(Localize("Home.Toast.ServiceModeOperationCanceled"), ToastType.Info);
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Service mode operation failed");
            result = ServiceModeOperationResult.Failed(exception.Message);
            RaiseToast(Localize(installOrUpdate
                ? "Home.Toast.ServiceModeInstallFailed"
                : "Home.Toast.ServiceModeUninstallFailed"), ToastType.Error);
        }
        finally
        {
            _isServiceModeBusy = false;
            _lastServiceModeProbe = null;
            try
            {
                await RefreshServiceModeAsync(token);
            }
            catch
            {
                if (!token.IsCancellationRequested)
                {
                    await ApplyServiceModeStatusAsync(ServiceModeStatus.Unavailable(string.Empty));
                }
            }

            Post(RaiseHomeStateChanged);
        }

        return result;
    }

    private void ApplyServiceModeStatus(ServiceModeStatus status)
    {
        _serviceModeStatus = status;
        RaiseHomeStateChanged();
    }

    private bool IsServiceModeCoreHostActive => _serviceModeStatus.IsRunning;

    private Task ApplyServiceModeStatusAsync(ServiceModeStatus status)
    {
        if (_uiContext is null || SynchronizationContext.Current == _uiContext)
        {
            ApplyServiceModeStatus(status);
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _uiContext.Post(_ =>
        {
            try
            {
                ApplyServiceModeStatus(status);
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }, null);
        return completion.Task;
    }

    private void InvalidateCoreVersion()
    {
        _shouldRefreshCoreVersion = true;
    }

    private void RaiseToast(string message, ToastType type)
    {
        if (string.IsNullOrWhiteSpace(message) || _isDisposed)
        {
            return;
        }

        ToastRequested?.Invoke(this, (message, type));
    }

    private async Task SetOutboundModeAsync(OutboundMode mode)
    {
        if (_outboundMode == mode)
        {
            return;
        }

        // 重启或更新期间运行时 API 不可用，所以拒绝乐观切换。
        if (!IsCoreInteractive)
        {
            return;
        }

        var previous = _outboundMode;
        _outboundMode = mode;
        RaiseHomeStateChanged();

        // 先更新 UI，再写入核心；MainWindow 统一持久化模式变化。
        if (_proxyClient is null)
        {
            return;
        }

        var applied = await _proxyClient.SetOutboundModeAsync(mode);
        if (!applied)
        {
            _outboundMode = previous;
            Post(RaiseHomeStateChanged);
        }
    }

    private void SetTakeoverTab(bool tun)
    {
        if (_isTakeoverTunTab == tun)
        {
            return;
        }

        _isTakeoverTunTab = tun;
        OnPropertyChanged(nameof(IsTakeoverProxyTabSelected));
        OnPropertyChanged(nameof(IsTakeoverTunTabSelected));
    }

    private void ResetTraffic()
    {
        if (_proxyClient is IRuntimeSnapshotClient runtimeClient)
        {
            _ = ResetRuntimeTrafficAsync(runtimeClient);
        }
        _directRuntimeTrafficTracker.ResetBaseline();
        _uploadSpeed = 0;
        _downloadSpeed = 0;
        _uploadTotal = 0;
        _downloadTotal = 0;
        // 重置回到首次进入时使用的零基线。
        SeedZeroHistory();
        RaiseHomeStateChanged();
    }

    private static async Task ResetRuntimeTrafficAsync(IRuntimeSnapshotClient runtimeClient)
    {
        try
        {
            await runtimeClient.ResetRuntimeTrafficAsync();
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Runtime traffic reset failed: {exception.Message}");
        }
    }

    private void Post(Action action)
    {
        if (_uiContext is not null)
        {
            _uiContext.Post(_ => action(), null);
        }
        else
        {
            action();
        }
    }

    // 生成并复制 shell 代理导出；核心或地址无效时静默忽略。
    public void CopyTerminalProxyCommand(TerminalShell shell)
    {
        if (!IsCoreRunning)
        {
            return;
        }

        var address = _proxyAddressValueText;
        if (string.IsNullOrWhiteSpace(address))
        {
            return;
        }

        var url = address.Contains("://", StringComparison.Ordinal) ? address : $"http://{address}";
        var command = shell switch
        {
            TerminalShell.PowerShell => $"$env:http_proxy=\"{url}\"; $env:https_proxy=\"{url}\"",
            TerminalShell.Cmd => $"set http_proxy={url} && set https_proxy={url}",
            TerminalShell.Bash => $"export http_proxy={url} && export https_proxy={url}",
            _ => null,
        };
        if (command is null)
        {
            return;
        }

        _clipboardWriter?.WriteText(command);
    }

    // 宿主注入平台信息；本层只映射显示文本。
    private string PlatformName() => _systemPlatform switch
    {
        SystemProxyPlatform.Windows => "Windows",
        SystemProxyPlatform.Linux => "Linux",
        SystemProxyPlatform.MacOS => "macOS",
        _ => Localize("Home.Platform.Unknown")
    };

    private static string FormatUptime(TimeSpan span)
    {
        return span.TotalDays >= 1
            ? $"{(int)span.TotalDays}d {span.Hours:00}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Hours:00}:{span.Minutes:00}:{span.Seconds:00}";
    }

    private void RaiseHomeStateChanged()
    {
        OnPropertyChanged(nameof(IsSystemProxyEnabled));
        OnPropertyChanged(nameof(IsTunEnabled));
        OnPropertyChanged(nameof(PrivilegeModeText));
        OnPropertyChanged(nameof(ServiceModeState));
        OnPropertyChanged(nameof(ServiceModeMessage));
        OnPropertyChanged(nameof(ServiceModeButtonText));
        OnPropertyChanged(nameof(IsServiceModeUpdateAvailable));
        OnPropertyChanged(nameof(CanToggleServiceMode));
        OnPropertyChanged(nameof(IsCoreRunning));
        OnPropertyChanged(nameof(CoreStatusValueText));
        OnPropertyChanged(nameof(OutboundMode));
        OnPropertyChanged(nameof(IsRuleOutboundSelected));
        OnPropertyChanged(nameof(IsGlobalOutboundSelected));
        OnPropertyChanged(nameof(IsDirectOutboundSelected));
        OnPropertyChanged(nameof(OutboundModeDescriptionText));
        OnPropertyChanged(nameof(UptimeValueText));
        OnPropertyChanged(nameof(MemoryValueText));
        OnPropertyChanged(nameof(PlatformValueText));
        OnPropertyChanged(nameof(CoreVersionValueText));
        OnPropertyChanged(nameof(ProxyAddressValueText));
        OnPropertyChanged(nameof(UploadSpeedValueText));
        OnPropertyChanged(nameof(DownloadSpeedValueText));
        OnPropertyChanged(nameof(UploadTotalValueText));
        OnPropertyChanged(nameof(DownloadTotalValueText));
        OnPropertyChanged(nameof(ActiveConnectionsValueText));
        OnPropertyChanged(nameof(UploadSamples));
        OnPropertyChanged(nameof(DownloadSamples));
        OnPropertyChanged(nameof(IsCoreRestarting));
        OnPropertyChanged(nameof(CanRestartCore));
        OnPropertyChanged(nameof(IsCoreUpdating));
        OnPropertyChanged(nameof(IsServiceModeBusy));
        OnPropertyChanged(nameof(CanUpdateCore));
        OnPropertyChanged(nameof(IsCoreInteractive));
        OnPropertyChanged(nameof(IsTunToggleEnabled));
        OnPropertyChanged(nameof(CoreSignalTag));
        OnPropertyChanged(nameof(IsNetworkConnected));
        OnPropertyChanged(nameof(IsWifiConnection));
        OnPropertyChanged(nameof(IsWiredConnection));
        OnPropertyChanged(nameof(NetworkSignalTag));
        OnPropertyChanged(nameof(NetworkTypeText));
        OnPropertyChanged(nameof(NetworkNameValueText));
    }

    private void OnLanguageChanged(object? sender, EventArgs args)
    {
        RaiseHomeStateChanged();
    }

    private string Localize(string key) => _localization?.GetString(key) ?? key;

    private static void PushSpeedSample(Queue<double> history, long speed)
    {
        history.Enqueue(speed);

        if (history.Count > SpeedHistoryCapacity)
        {
            history.Dequeue();
        }
    }

    private void ApplySpeedHistory(IReadOnlyList<CoreRuntimeSample> history)
    {
        _uploadSpeedHistory.Clear();
        _downloadSpeedHistory.Clear();
        for (var i = history.Count; i < SpeedHistoryCapacity; i++)
        {
            _uploadSpeedHistory.Enqueue(0);
            _downloadSpeedHistory.Enqueue(0);
        }

        foreach (var sample in history.TakeLast(SpeedHistoryCapacity))
        {
            _uploadSpeedHistory.Enqueue(sample.UploadSpeed);
            _downloadSpeedHistory.Enqueue(sample.DownloadSpeed);
        }
    }

    private void SeedZeroHistory()
    {
        _uploadSpeedHistory.Clear();
        _downloadSpeedHistory.Clear();
        for (var i = 0; i < SpeedHistoryCapacity; i++)
        {
            _uploadSpeedHistory.Enqueue(0);
            _downloadSpeedHistory.Enqueue(0);
        }

        UploadSamples = _uploadSpeedHistory.ToArray();
        DownloadSamples = _downloadSpeedHistory.ToArray();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        if (_localization is not null)
        {
            _localization.LanguageChanged -= OnLanguageChanged;
        }
        _systemProxyService.StatusChanged -= OnSystemProxyStatusChanged;

        var cancellation = _refreshCancellation;
        _refreshCancellation = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
    }
}
