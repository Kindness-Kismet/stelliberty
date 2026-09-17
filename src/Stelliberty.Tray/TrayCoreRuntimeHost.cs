using Stelliberty.Infrastructure.Tray;
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

namespace Stelliberty.Tray;

internal interface ITrayCoreRuntime
{
    event EventHandler<TrayCoreStatus>? StateChanged;

    event EventHandler<TrayCoreLogEntry>? LogReceived;

    TrayCoreStatus CurrentStatus { get; }

    Task<TrayCoreOperationResult> EnsureStartedAsync(CancellationToken cancellationToken);

    Task<TrayCoreOperationResult> StopAsync(CancellationToken cancellationToken);

    Task<CoreApplyConfigResult> ApplyConfigAsync(
        CoreApplyConfigRequest request,
        CancellationToken cancellationToken);

    Task RestartAsync(CancellationToken cancellationToken);

    Task<CoreApplyConfigResult> ApplyCurrentSettingsAsync(CancellationToken cancellationToken);

    Task<ServiceModeStatus> GetServiceModeStatusAsync(CancellationToken cancellationToken);
    Task SendHeartbeatAsync(CancellationToken cancellationToken);

    Task<ServiceModeOperationResult> InstallOrUpdateServiceModeAsync(CancellationToken cancellationToken);

    Task<ServiceModeOperationResult> UninstallServiceModeAsync(CancellationToken cancellationToken);
}

internal sealed class TrayCoreRuntimeHost : ITrayCoreRuntime, IAsyncDisposable
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly CoreLogJournal _logs;
    private readonly ServiceModeManager _serviceModeManager;
    private readonly CoreProcessCleaner _coreProcessCleaner;
    private SwitchableCoreManager? _manager;
    private ServiceModeSessionSwitcher? _serviceModeSwitcher;
    private readonly PipeCoreProxyClient _selectionClient = new(TrayCoreEndpoints.Core);
    private ProxySelectionRestoringCoreManager? _restoringManager;
    private TrayCoreStatus _status = new(
        new CoreSnapshot(CoreState.Unavailable, null, TrayCoreEndpoints.Core, null),
        0);
    private int? _lastCorePid;
    private bool _isHubStarted;
    private bool _isServiceModeActive;
    private volatile bool _isShutdownSuspended;
    private bool _isDisposed;

    public TrayCoreRuntimeHost(
        CoreLogJournal logs,
        ServiceModeManager? serviceModeManager = null,
        CoreProcessCleaner? coreProcessCleaner = null)
    {
        _logs = logs;
        _serviceModeManager = serviceModeManager ?? new ServiceModeManager(new ServiceModePaths(
            TrayApplicationLayout.ServiceDirectory,
            TrayApplicationLayout.ServiceUpdateBinaryPath,
            TrayApplicationLayout.ServiceInstalledBinaryPath));
        _coreProcessCleaner = coreProcessCleaner ?? new CoreProcessCleaner(TrayApplicationLayout.ServiceDirectory);
    }

    public event EventHandler<TrayCoreStatus>? StateChanged;

    public event EventHandler<TrayCoreLogEntry>? LogReceived;

    public TrayCoreStatus CurrentStatus
    {
        get
        {
            lock (_stateGate)
            {
                return _status;
            }
        }
    }

    public async Task<TrayCoreOperationResult> EnsureStartedAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            return await EnsureStartedCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppLogger.Error(exception, "Tray core startup failed");
            var failed = UpdateStatus(new CoreSnapshot(
                CoreState.Unavailable,
                null,
                TrayCoreEndpoints.Core,
                exception.Message));
            return new TrayCoreOperationResult(false, exception.Message, failed);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<TrayCoreOperationResult> StopAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_manager is null)
            {
                return new TrayCoreOperationResult(true, "Core is not started.", CurrentStatus);
            }

            ServiceModeOperationResult? serviceResult = null;
            BootstrapResult? normalResult = null;
            if (_isServiceModeActive)
            {
                serviceResult = await _serviceModeManager.StopCoreHostAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (_isHubStarted)
            {
                normalResult = await Task.Run(HubBootstrap.StopCore, cancellationToken).ConfigureAwait(false);
            }

            var snapshot = await _manager.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            var isSuccess = serviceResult?.IsSuccess ?? normalResult?.Ok ?? true;
            var message = serviceResult?.Message ?? normalResult?.Message ?? "Core is not started.";
            return new TrayCoreOperationResult(isSuccess, message, UpdateStatus(snapshot));
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
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var manager = RequireManager();
            var result = await _restoringManager!.ApplyConfigAsync(request, cancellationToken).ConfigureAwait(false);
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

    public Task<CoreApplyConfigResult> ApplyCurrentSettingsAsync(CancellationToken cancellationToken)
    {
        var content = BuildBootstrapYaml(CanUseTun() || _isServiceModeActive);
        var subscriptionId = new FileSubscriptionSelectionStore(TrayApplicationLayout.AppDataDirectory)
            .GetCurrentSubscriptionId() ?? string.Empty;
        return ApplyConfigAsync(new CoreApplyConfigRequest(content, subscriptionId), cancellationToken);
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

    private async Task<TrayCoreOperationResult> EnsureStartedCoreAsync(CancellationToken cancellationToken)
    {
        if (_manager is not null)
        {
            var current = await _manager.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (current.State == CoreState.Running)
            {
                return new TrayCoreOperationResult(true, "Core is already running.", UpdateStatus(current));
            }

            var resumed = _isServiceModeActive
                ? await StartServiceCoreAsync(
                    await _serviceModeManager.GetStatusAsync(cancellationToken).ConfigureAwait(false),
                    cancellationToken).ConfigureAwait(false)
                : await ResumeNormalCoreAsync(cancellationToken).ConfigureAwait(false);
            if (!resumed.IsSuccess)
            {
                return new TrayCoreOperationResult(false, resumed.Message, CurrentStatus);
            }

            await _manager.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            var resumedStatus = UpdateStatus(await _manager.GetSnapshotAsync(cancellationToken).ConfigureAwait(false));
            return new TrayCoreOperationResult(true, resumed.Message, resumedStatus);
        }

        var serviceStatus = await GetInitialServiceStatusAsync(cancellationToken).ConfigureAwait(false);
        var settingsStore = new JsonAppSettingsStore(new TrayPlatformDirectories());
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
                return new TrayCoreOperationResult(false, started.Message, CurrentStatus);
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
                return new TrayCoreOperationResult(false, cleanup.Message, CurrentStatus);
            }

            var started = await ResumeNormalCoreAsync(cancellationToken).ConfigureAwait(false);
            if (!started.IsSuccess)
            {
                return new TrayCoreOperationResult(false, started.Message, CurrentStatus);
            }

            initialManager = CreateNormalCoreManager();
            message = started.Message;
        }

        InitializeCoreManager(initialManager);
        await _manager!.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        await _restoringManager!.RestoreCurrentCoreSelectionsAsync("tray-startup", cancellationToken).ConfigureAwait(false);
        var status = UpdateStatus(await _manager.GetSnapshotAsync(cancellationToken).ConfigureAwait(false));
        AppLogger.Info($"Tray owns core: pid={status.Snapshot.Pid} generation={status.CoreGeneration} service={_isServiceModeActive}");
        return new TrayCoreOperationResult(true, message, status);
    }

    private void InitializeCoreManager(ICoreManager initialManager)
    {
        _manager = new SwitchableCoreManager(initialManager);
        var selectionStore = new FileSubscriptionSelectionStore(TrayApplicationLayout.AppDataDirectory);
        var source = new FileRuntimeProxyConfigSource(TrayApplicationLayout.RuntimeDirectory, selectionStore);
        var provider = new MihomoApiProxyConfigProvider(_selectionClient);
        var syncState = new ProxySelectionSyncState();
        var selections = new StoredProxySelectionConfigProvider(provider,
            new FileProxySelectionStore(TrayApplicationLayout.AppDataDirectory), selectionStore, syncState);
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
        var settings = new JsonAppSettingsStore(new TrayPlatformDirectories()).Load();
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

    private ICoreManager CreateNormalCoreManager() => new IpcCoreManager(TrayCoreEndpoints.Hub);

    private ICoreManager CreateServiceCoreManager(ServiceModeStatus _)
    {
        return new ServiceModeCoreManager(
            _serviceModeManager,
            TrayCoreEndpoints.Core,
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
                TrayApplicationLayout.CoreBinaryPath,
                TrayApplicationLayout.CoreDirectory,
                WriteServiceModeActiveConfig(BuildInitialBootstrapYaml(canUseTun: true))),
            cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? CoreHostOperationResult.Success(result.Message)
            : CoreHostOperationResult.Failure(result.Message);
    }

    private static BootstrapResult StartHub()
    {
        return HubBootstrap.Start(new BootstrapOptions(
            PipeName: TrayCoreEndpoints.Hub,
            CorePath: TrayApplicationLayout.CoreBinaryPath,
            DataCoreDir: TrayApplicationLayout.CoreDirectory,
            UserDataDir: TrayApplicationLayout.AppDataDirectory,
            CorePipe: TrayCoreEndpoints.Core,
            BootstrapYaml: BuildInitialBootstrapYaml(CanUseTun())));
    }

    private static string BuildInitialBootstrapYaml(bool canUseTun)
    {
        try
        {
            return BuildBootstrapYaml(canUseTun);
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Tray startup config generation failed: {exception.Message}");
            return StartupBootstrapConfigBuilder.BuildDefaultEmptyYaml(TrayCoreEndpoints.Core);
        }
    }

    private static string BuildBootstrapYaml(bool canUseTun)
    {
        var directories = new TrayPlatformDirectories();
        var settingsStore = new JsonAppSettingsStore(directories);
        var selectionStore = new FileSubscriptionSelectionStore(directories.AppDataDirectory);
        var subscriptionStore = new FileSubscriptionStore(directories.AppDataDirectory);
        var overrideStore = new FileOverrideStore(directories.AppDataDirectory);
        var ruleOverrideService = new RuleOverrideService(
            subscriptionStore,
            selectionStore,
            new FileRuleOverrideStore(directories.AppDataDirectory),
            new RuleParser());
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
        return builder.Build(TrayCoreEndpoints.Core, canUseTun);
    }

    private static string WriteServiceModeActiveConfig(string content)
    {
        // 服务重载配置必须位于核心目录内，满足 mihomo 的 SAFE_PATHS 约束。
        Directory.CreateDirectory(TrayApplicationLayout.CoreDirectory);
        var path = Path.Combine(TrayApplicationLayout.CoreDirectory, "_service_active.yaml");
        File.WriteAllText(path, ServiceModeRuntimeConfigWriter.Write(content, TrayCoreEndpoints.Core));
        return path;
    }

    private static bool CanUseTun()
    {
        return AppSettingsNormalizer.CanUseTun(
            new SystemProcessPrivilegeProbe().Detect(),
            hasServiceTunHost: false);
    }

    private TrayCoreStatus UpdateStatus(CoreSnapshot snapshot)
    {
        TrayCoreStatus status;
        lock (_stateGate)
        {
            var generation = _status.CoreGeneration;
            if (snapshot.Pid is { } pid && pid != _lastCorePid)
            {
                _lastCorePid = pid;
                generation++;
                AppLogger.Info($"Core generation advanced: pid={pid} generation={generation}");
            }

            status = new TrayCoreStatus(snapshot, generation);
            _status = status;
        }

        StateChanged?.Invoke(this, status);
        return status;
    }

    private void OnCoreStateChanged(object? sender, CoreSnapshot snapshot) => UpdateStatus(snapshot);

    private void OnCoreLogReceived(object? sender, Stelliberty.Domain.CoreLogs.CoreLogMessage message)
    {
        var entry = _logs.Append(CurrentStatus.CoreGeneration, message);
        LogReceived?.Invoke(this, entry);
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
                var result = await _serviceModeSwitcher.PrepareForShutdownAsync(timeout.Token).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    AppLogger.Warning($"Service-mode core shutdown failed: {result.Message}");
                }
                _serviceModeSwitcher.Dispose();
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
        }
        finally
        {
            _operationGate.Release();
            _operationGate.Dispose();
        }
    }

    private SwitchableCoreManager RequireManager() =>
        _manager ?? throw new InvalidOperationException("Tray core runtime is not started.");

    private ServiceModeSessionSwitcher RequireServiceModeSwitcher() =>
        _serviceModeSwitcher ?? throw new InvalidOperationException("Tray service-mode runtime is not started.");

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
