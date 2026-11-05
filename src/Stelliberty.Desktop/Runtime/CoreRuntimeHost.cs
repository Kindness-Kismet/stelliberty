using Stelliberty.Desktop.Services;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Runtime;
using Stelliberty.Application.Settings;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Infrastructure.Core;
using Stelliberty.Infrastructure.Overrides;
using Stelliberty.Infrastructure.Platform;
using Stelliberty.Infrastructure.Runtime;
using Stelliberty.Infrastructure.Settings;
using Stelliberty.Infrastructure.Storage;
using Stelliberty.Infrastructure.Subscriptions;
using Stelliberty.Native.Hub;
using Stelliberty.Application.Rules;
using Stelliberty.Infrastructure.Rules;
using Stelliberty.Application.Proxies;
using Stelliberty.Infrastructure.Proxies;

namespace Stelliberty.Desktop.Runtime;

internal sealed partial class CoreRuntimeHost : IReadyCoreManager, IServiceModeManager, ISubscriptionProviderSource, IAsyncDisposable
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly ServiceModeManager _serviceModeManager;
    private readonly CoreProcessCleaner _coreProcessCleaner;
    private SwitchableCoreManager? _manager;
    private ServiceModeSessionSwitcher? _serviceModeSwitcher;
    private readonly PipeCoreProxyClient _selectionClient = new(CoreEndpoints.Core);
    private ProxySelectionRestoringCoreManager? _restoringManager;
    private CoreRuntimeStatus _status = new(
        new CoreSnapshot(CoreState.Unavailable, null, CoreEndpoints.Core, null),
        0);
    private int? _lastCorePid;
    private bool _isHubStarted;
    private bool _isServiceModeActive;
    private volatile bool _isShutdownSuspended;
    private bool _isDisposed;

    public CoreRuntimeHost(
        ServiceModeManager? serviceModeManager = null,
        CoreProcessCleaner? coreProcessCleaner = null)
    {
        _serviceModeManager = serviceModeManager ?? new ServiceModeManager(new ServiceModePaths(
            DesktopApplicationLayout.ServiceDirectory,
            DesktopApplicationLayout.ServiceUpdateBinaryPath,
            DesktopApplicationLayout.ServiceInstalledBinaryPath));
        _coreProcessCleaner = coreProcessCleaner ?? new CoreProcessCleaner(DesktopApplicationLayout.ServiceDirectory);
    }

    public event EventHandler<CoreRuntimeStatus>? RuntimeStateChanged;

    public event EventHandler<CoreSnapshot>? StateChanged;

    public event EventHandler<Stelliberty.Domain.CoreLogs.CoreLogMessage>? CoreLogReceived;

    public CoreRuntimeStatus CurrentStatus
    {
        get
        {
            lock (_stateGate)
            {
                return _status;
            }
        }
    }

    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        var result = await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess) throw new InvalidOperationException(result.Message);
    }

    public Task<CoreSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CurrentStatus.Snapshot);
    }

    Task<ServiceModeStatus> IServiceModeManager.GetStatusAsync(CancellationToken cancellationToken) =>
        GetServiceModeStatusAsync(cancellationToken);

    Task<ServiceModeOperationResult> IServiceModeManager.InstallOrUpdateAsync(CancellationToken cancellationToken) =>
        InstallOrUpdateServiceModeAsync(cancellationToken);

    Task<ServiceModeOperationResult> IServiceModeManager.UninstallAsync(CancellationToken cancellationToken) =>
        UninstallServiceModeAsync(cancellationToken);

    Task<SubscriptionProviderSnapshot> ISubscriptionProviderSource.ReadAsync(string subscriptionId, CancellationToken cancellationToken) =>
        ReadProvidersAsync(subscriptionId, cancellationToken);

    Task ISubscriptionProviderSource.SyncAsync(string subscriptionId, string providerType, string providerName, CancellationToken cancellationToken) =>
        SyncProviderAsync(subscriptionId, providerType, providerName, cancellationToken);

    public async Task<CoreOperationResult> EnsureStartedAsync(CancellationToken cancellationToken)
    {
        RecordCoreRunIntent(true);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            return await EnsureStartedCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppLogger.Error(exception, "Desktop core startup failed");
            var failed = UpdateStatus(new CoreSnapshot(
                CoreState.Unavailable,
                null,
                CoreEndpoints.Core,
                exception.Message));
            return new CoreOperationResult(false, exception.Message, failed);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<CoreApplyConfigResult> ApplyConfigAsync(
        CoreApplyConfigRequest request,
        CancellationToken cancellationToken)
    {
        RecordCoreRunIntent(true);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var manager = RequireManager();
            var providerContext = string.IsNullOrWhiteSpace(request.SubscriptionId) ? null
                : _providerSnapshots.CreateContext(request.SubscriptionId, request.RuntimeYamlContent, request.SubscriptionContentFingerprint);
            _providerContext = null;
            var result = await _restoringManager!.ApplyConfigAsync(request, cancellationToken).ConfigureAwait(false);
            _providerContext = providerContext;
            UpdateStatus(await manager.GetSnapshotAsync(cancellationToken).ConfigureAwait(false));
            return result;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task RestartAsync(CancellationToken cancellationToken)
    {
        RecordCoreRunIntent(true, invalidateRecovery: true);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var manager = RequireManager();
            await _restoringManager!.RestartAsync(cancellationToken).ConfigureAwait(false);
            UpdateStatus(await manager.GetSnapshotAsync(cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<CoreApplyConfigResult> ApplyCurrentSettingsAsync(CancellationToken cancellationToken)
    {
        // 托盘操作与主窗口共用线程，配置读取和覆写计算必须在后台执行。
        var request = await Task.Run(() =>
        {
            var content = BuildBootstrapYaml(CanUseTun() || _isServiceModeActive);
            var subscriptionId = new FileSubscriptionSelectionStore(DesktopApplicationLayout.AppDataDirectory)
                .GetCurrentSubscriptionId() ?? string.Empty;
            return new CoreApplyConfigRequest(content, subscriptionId);
        }, cancellationToken).ConfigureAwait(false);
        return await ApplyConfigAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public Task<ServiceModeStatus> GetServiceModeStatusAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _serviceModeManager.GetStatusAsync(cancellationToken);
    }

    public async Task SendHeartbeatAsync(CancellationToken cancellationToken)
    {
        if (_isServiceModeActive && !_isShutdownSuspended)
        {
            var result = await _serviceModeManager.SendHeartbeatAsync(cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                AppLogger.Warning($"Service heartbeat failed: {result.Message}");
            }
        }
    }

    public async Task<ServiceModeOperationResult> InstallOrUpdateServiceModeAsync(CancellationToken cancellationToken)
    {
        RecordCoreRunIntent(true, invalidateRecovery: true);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var ready = await EnsureStartedCoreAsync(cancellationToken).ConfigureAwait(false);
            if (!ready.IsSuccess)
            {
                return ServiceModeOperationResult.Failed(ready.Message);
            }

            var result = await _serviceModeManager.InstallOrUpdateAsync(cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return result;
            }

            var activation = await RequireServiceModeSwitcher().ActivateAsync(cancellationToken).ConfigureAwait(false);
            if (_manager is not null)
            {
                UpdateStatus(await _manager.GetSnapshotAsync(CancellationToken.None).ConfigureAwait(false));
            }
            return activation;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<ServiceModeOperationResult> UninstallServiceModeAsync(CancellationToken cancellationToken)
    {
        RecordCoreRunIntent(true, invalidateRecovery: true);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var result = await _serviceModeManager.UninstallAsync(cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess || !_isServiceModeActive)
            {
                return result;
            }

            // 服务已经卸载后必须完成普通核心恢复，不再接受原操作取消。
            var deactivation = await RequireServiceModeSwitcher().DeactivateAsync(CancellationToken.None).ConfigureAwait(false);
            if (_manager is not null)
            {
                UpdateStatus(await _manager.GetSnapshotAsync(CancellationToken.None).ConfigureAwait(false));
            }
            return deactivation;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<CoreOperationResult> EnsureStartedCoreAsync(CancellationToken cancellationToken)
    {
        if (_manager is not null)
        {
            var current = await _manager.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (current.State == CoreState.Running)
            {
                return new CoreOperationResult(true, "Core is already running.", UpdateStatus(current));
            }

            var resumed = _isServiceModeActive
                ? await StartServiceCoreAsync(
                    await _serviceModeManager.GetStatusAsync(cancellationToken).ConfigureAwait(false),
                    cancellationToken).ConfigureAwait(false)
                : await ResumeNormalCoreAsync(cancellationToken).ConfigureAwait(false);
            if (!resumed.IsSuccess)
            {
                return new CoreOperationResult(false, resumed.Message, CurrentStatus);
            }

            await _manager.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            var resumedStatus = UpdateStatus(await _manager.GetSnapshotAsync(cancellationToken).ConfigureAwait(false));
            return new CoreOperationResult(true, resumed.Message, resumedStatus);
        }

        var serviceStatus = await GetInitialServiceStatusAsync(cancellationToken).ConfigureAwait(false);
        var settingsStore = new JsonAppSettingsStore(new DesktopPlatformDirectories());
        var settings = settingsStore.Load();
        if (AppSettingsNormalizer.RevokeTunIfUnavailable(settings,
            new SystemProcessPrivilegeProbe().Detect(), serviceStatus.IsRunning))
        {
            settingsStore.Save(settings);
        }
        ICoreManager initialManager;
        string message;
        if (serviceStatus.IsRunning)
        {
            var started = await StartServiceCoreAsync(serviceStatus, cancellationToken).ConfigureAwait(false);
            if (!started.IsSuccess)
            {
                return new CoreOperationResult(false, started.Message, CurrentStatus);
            }

            _isServiceModeActive = true;
            initialManager = CreateServiceCoreManager(serviceStatus);
            message = started.Message;
        }
        else
        {
            var cleanup = _coreProcessCleaner.CleanupForNormalMode(serviceStatus);
            if (!cleanup.IsSuccess)
            {
                return new CoreOperationResult(false, cleanup.Message, CurrentStatus);
            }

            var started = await ResumeNormalCoreAsync(cancellationToken).ConfigureAwait(false);
            if (!started.IsSuccess)
            {
                return new CoreOperationResult(false, started.Message, CurrentStatus);
            }

            initialManager = CreateNormalCoreManager();
            message = started.Message;
        }

        InitializeCoreManager(initialManager);
        await _manager!.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        await _restoringManager!.RestoreCurrentCoreSelectionsAsync("tray-startup", cancellationToken).ConfigureAwait(false);
        var status = UpdateStatus(await _manager.GetSnapshotAsync(cancellationToken).ConfigureAwait(false));
        AppLogger.Info($"Desktop owns core: pid={status.Snapshot.Pid} generation={status.CoreGeneration} service={_isServiceModeActive}");
        return new CoreOperationResult(true, message, status);
    }

    private void InitializeCoreManager(ICoreManager initialManager)
    {
        _manager = new SwitchableCoreManager(initialManager);
        var selectionStore = new FileSubscriptionSelectionStore(DesktopApplicationLayout.AppDataDirectory);
        var source = new FileRuntimeProxyConfigSource(DesktopApplicationLayout.RuntimeDirectory, selectionStore);
        var provider = new MihomoApiProxyConfigProvider(_selectionClient);
        var syncState = new ProxySelectionSyncState();
        var selections = new StoredProxySelectionConfigProvider(provider,
            new FileProxySelectionStore(DesktopApplicationLayout.AppDataDirectory), selectionStore, syncState);
        var restorer = new ProxySelectionRestorer(_selectionClient, provider,
            new FileRuntimeProxyConfigProvider(new ProxyConfigLoader(source, new ProxyConfigParser())),
            selections, syncState, selectionStore);
        _restoringManager = new ProxySelectionRestoringCoreManager(_manager, restorer);
        _manager.StateChanged += OnCoreStateChanged;
        _manager.CoreLogReceived += OnCoreLogReceived;
        _serviceModeSwitcher = new ServiceModeSessionSwitcher(
            _serviceModeManager,
            _manager,
            CreateServiceCoreManager,
            CreateNormalCoreManager,
            StopNormalCoreAsync,
            ResumeNormalCoreAsync,
            StartServiceCoreAsync,
            isActive => _isServiceModeActive = isActive,
            _isServiceModeActive);
    }

    private async Task<ServiceModeStatus> GetInitialServiceStatusAsync(CancellationToken cancellationToken)
    {
        var status = await _serviceModeManager.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        var settings = new JsonAppSettingsStore(new DesktopPlatformDirectories()).Load();
        if (!settings.IsTunEnabled || status.IsRunning || !status.IsInstalled)
        {
            return status;
        }

        // 登录时服务可能仍在启动，沿用最多 6 秒的 TUN 服务等待窗口。
        var deadline = DateTimeOffset.UtcNow.AddSeconds(6);
        while (DateTimeOffset.UtcNow < deadline && status.IsInstalled && !status.IsRunning)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            status = await _serviceModeManager.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        }
        return status;
    }

    private ICoreManager CreateNormalCoreManager() => new IpcCoreManager(CoreEndpoints.Hub);

    private ICoreManager CreateServiceCoreManager(ServiceModeStatus _)
    {
        return new ServiceModeCoreManager(
            _serviceModeManager,
            CoreEndpoints.Core,
            WriteServiceModeActiveConfig);
    }

    private async Task<CoreHostOperationResult> StopNormalCoreAsync(CancellationToken cancellationToken)
    {
        if (!_isHubStarted)
        {
            return CoreHostOperationResult.Success("Normal core is not started.");
        }

        var result = await Task.Run(HubBootstrap.StopCore, cancellationToken).ConfigureAwait(false);
        return result.Ok
            ? CoreHostOperationResult.Success(result.Message)
            : CoreHostOperationResult.Failure(result.Message);
    }

    private async Task<CoreHostOperationResult> ResumeNormalCoreAsync(CancellationToken cancellationToken)
    {
        var result = await Task.Run(
            () => _isHubStarted ? HubBootstrap.StartCore() : StartHub(),
            cancellationToken).ConfigureAwait(false);
        if (result.Ok)
        {
            _isHubStarted = true;
            return CoreHostOperationResult.Success(result.Message);
        }

        return CoreHostOperationResult.Failure(result.Message);
    }

    private async Task<CoreHostOperationResult> StartServiceCoreAsync(
        ServiceModeStatus status,
        CancellationToken cancellationToken)
    {
        var cleanup = _coreProcessCleaner.CleanupForServiceMode(status);
        if (!cleanup.IsSuccess)
        {
            return CoreHostOperationResult.Failure(cleanup.Message);
        }

        var result = await _serviceModeManager.StartCoreHostAsync(
            new ServiceModeCoreHostRequest(
                DesktopApplicationLayout.CoreBinaryPath,
                DesktopApplicationLayout.CoreDirectory,
                WriteServiceModeActiveConfig(BuildInitialBootstrapYaml(canUseTun: true))),
            cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? CoreHostOperationResult.Success(result.Message)
            : CoreHostOperationResult.Failure(result.Message);
    }

    private BootstrapResult StartHub()
    {
        return HubBootstrap.Start(new BootstrapOptions(
            PipeName: CoreEndpoints.Hub,
            CorePath: DesktopApplicationLayout.CoreBinaryPath,
            DataCoreDir: DesktopApplicationLayout.CoreDirectory,
            UserDataDir: DesktopApplicationLayout.AppDataDirectory,
            CorePipe: CoreEndpoints.Core,
            BootstrapYaml: BuildInitialBootstrapYaml(CanUseTun())));
    }

    private string BuildInitialBootstrapYaml(bool canUseTun)
    {
        try
        {
            var content = BuildBootstrapYaml(canUseTun);
            SetProviderContext(new FileSubscriptionSelectionStore(DesktopApplicationLayout.AppDataDirectory).GetCurrentSubscriptionId(), content);
            return content;
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Desktop startup config generation failed: {exception.Message}");
            _providerContext = null;
            return StartupBootstrapConfigBuilder.BuildDefaultEmptyYaml(CoreEndpoints.Core);
        }
    }

    private static string BuildBootstrapYaml(bool canUseTun)
    {
        var directories = new DesktopPlatformDirectories();
        var settingsStore = new JsonAppSettingsStore(directories);
        var selectionStore = new FileSubscriptionSelectionStore(directories.AppDataDirectory);
        var subscriptionStore = new FileSubscriptionStore(directories.AppDataDirectory);
        var overrideStore = new FileOverrideStore(directories.AppDataDirectory);
        var ruleOverrideService = new RuleOverrideService(
            subscriptionStore,
            selectionStore,
            new FileRuleOverrideStore(directories.AppDataDirectory),
            new RuleParser(),
            new FileRuleBaselineConfigSource(directories.RuntimeDirectory));
        var runtimeStore = new FileRuntimeConfigStore(directories.RuntimeDirectory);
        var builder = new StartupBootstrapConfigBuilder(
            settingsStore,
            selectionStore,
            new SelectedRuntimeFallbackGenerator(
                subscriptionStore,
                new SubscriptionOverrideSelectionUpdater(subscriptionStore),
                new SelectedSubscriptionRuntimeGenerator(
                    subscriptionStore,
                    selectionStore,
                    new RuntimeConfigGenerator(new HubOverrideEngine()),
                    overrideStore,
                    runtimeStore,
                    ruleOverrideService: ruleOverrideService)),
            new SubscriptionFailureRecorder(subscriptionStore));
        return builder.Build(CoreEndpoints.Core, canUseTun);
    }

    private static string WriteServiceModeActiveConfig(string content)
    {
        // 服务重载配置必须位于核心目录内，满足 mihomo 的 SAFE_PATHS 约束。
        Directory.CreateDirectory(DesktopApplicationLayout.CoreDirectory);
        var path = Path.Combine(DesktopApplicationLayout.CoreDirectory, "_service_active.yaml");
        File.WriteAllText(path, ServiceModeRuntimeConfigWriter.Write(content, CoreEndpoints.Core));
        return path;
    }

    private static bool CanUseTun()
    {
        return AppSettingsNormalizer.CanUseTun(
            new SystemProcessPrivilegeProbe().Detect(),
            hasServiceTunHost: false);
    }

    private CoreRuntimeStatus UpdateStatus(CoreSnapshot snapshot)
    {
        CoreRuntimeStatus status;
        lock (_stateGate)
        {
            var generation = _status.CoreGeneration;
            if (snapshot.Pid is { } pid && pid != _lastCorePid)
            {
                _lastCorePid = pid;
                generation++;
                AppLogger.Info($"Core generation advanced: pid={pid} generation={generation}");
            }

            // 计时起点随核心状态同步，窗口显隐和重复查询不能重新计时。
            var runningSince = snapshot.State == CoreState.Running
                ? snapshot.Pid == _status.Snapshot.Pid
                    ? _status.Snapshot.RunningSince ?? DateTimeOffset.UtcNow
                    : DateTimeOffset.UtcNow
                : (DateTimeOffset?)null;
            status = new CoreRuntimeStatus(snapshot with { RunningSince = runningSince }, generation);
            _status = status;
        }

        RuntimeStateChanged?.Invoke(this, status);
        StateChanged?.Invoke(this, status.Snapshot);
        return status;
    }

    private void OnCoreStateChanged(object? sender, CoreSnapshot snapshot) => UpdateStatus(snapshot);

    private void OnCoreLogReceived(object? sender, Stelliberty.Domain.CoreLogs.CoreLogMessage message)
    {
        CoreLogReceived?.Invoke(this, message);
    }

    public void SetShutdownSuspension(bool isSuspended)
    {
        _isShutdownSuspended = isSuspended;
        _manager?.SetShutdownSuspension(isSuspended);
    }

    public async ValueTask DisposeAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            if (_serviceModeSwitcher is not null)
            {
                using var timeout = new CancellationTokenSource(ShutdownTimeout);
                try
                {
                    var result = await _serviceModeSwitcher.PrepareForShutdownAsync(timeout.Token).ConfigureAwait(false);
                    if (!result.IsSuccess) AppLogger.Warning($"Service-mode core shutdown failed: {result.Message}");
                }
                catch (Exception exception)
                {
                    AppLogger.Warning($"Service-mode core shutdown failed: {exception.Message}");
                }
                await _serviceModeSwitcher.DisposeAsync().ConfigureAwait(false);
            }

            if (_manager is not null)
            {
                _restoringManager?.Dispose();
                _manager.StateChanged -= OnCoreStateChanged;
                _manager.CoreLogReceived -= OnCoreLogReceived;
                await _manager.DisposeAsync().ConfigureAwait(false);
            }

            await Task.Run(HubBootstrap.Shutdown).ConfigureAwait(false);
            _selectionClient.Dispose();
            _providerClient.Dispose();
        }
        finally
        {
            _operationGate.Release();
            _operationGate.Dispose();
        }
    }

    private SwitchableCoreManager RequireManager() =>
        _manager ?? throw new InvalidOperationException("Desktop core runtime is not started.");

    private ServiceModeSessionSwitcher RequireServiceModeSwitcher() =>
        _serviceModeSwitcher ?? throw new InvalidOperationException("Desktop service-mode runtime is not started.");

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
