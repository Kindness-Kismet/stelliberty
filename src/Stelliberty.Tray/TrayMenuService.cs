using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Localization;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Runtime;
using Stelliberty.Application.Settings;
using Stelliberty.Domain.Proxies;
using Stelliberty.Infrastructure.Localization;
using Stelliberty.Infrastructure.Platform;
using Stelliberty.Infrastructure.Proxies;
using Stelliberty.Infrastructure.Settings;
using Stelliberty.Infrastructure.Tray;

namespace Stelliberty.Tray;

internal interface ITrayHotkeyRuntime
{
    Task<GlobalHotkeyApplyResult> ApplyAsync(
        GlobalHotkeyAction action,
        string gesture,
        CancellationToken cancellationToken);

    Task SetSuppressedAsync(Guid connectionId, bool isSuppressed, CancellationToken cancellationToken);

    Task ReleaseSuppressionAsync(Guid connectionId);

#if DEBUG
    Task<object> ExecuteMenuDebugAsync(TrayMenuDebugRequest request, CancellationToken cancellationToken);
    Task<object> GetProxyMenuAsync(CancellationToken cancellationToken);
    Task SelectProxyAsync(string groupName, string nodeName, CancellationToken cancellationToken);

    Task CopyTerminalProxyAsync(CancellationToken cancellationToken);

    Task<bool> SimulateActivationAsync(GlobalHotkeyAction action, CancellationToken cancellationToken);
#endif
}

internal sealed class TrayMenuService(
    UiSessionManager uiSessions,
    ITrayCoreRuntime coreRuntime,
    ITrayRuntimeMonitor runtimeMonitor,
    TrayProxyCatalog proxyCatalog,
    ISystemProxyController systemProxy,
    TrayLifetime lifetime) : ITrayHotkeyRuntime, IAsyncDisposable
{
    private static readonly TimeSpan TrayDoubleClickThreshold = TimeSpan.FromMilliseconds(500);
    private readonly JsonAppSettingsStore _settingsStore = new(new TrayPlatformDirectories());
    private readonly PipeCoreProxyClient _proxyClient = new(Stelliberty.Infrastructure.Tray.TrayCoreEndpoints.Core);
    private readonly ProcessRunMode _runMode = new SystemProcessPrivilegeProbe().Detect();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly HashSet<Guid> _hotkeySuppressors = [];
    private readonly TrayClipboard _clipboard = new();
    private readonly SemaphoreSlim _runtimeOperationGate = new(1, 1);
    private JsonLocalizationService? _localization;
    private IGlobalHotkeyService? _globalHotkeyService;
    private TrayIcon? _trayIcon;
    private WindowIcon? _icon;
    private TrayIconState _iconState = TrayIconState.Disabled;
    private NativeMenuItem? _showItem;
    private NativeMenuItem? _copyItem;
    private NativeMenuItem? _copyPowerShellItem;
    private NativeMenuItem? _copyCmdItem;
    private NativeMenuItem? _copyBashItem;
    private NativeMenuItem? _outboundItem;
    private TrayProxyMenu? _proxyMenu;
#if DEBUG
    private DebugTrayMenu? _debugMenu;
#endif
    private NativeMenuItem? _outboundRuleItem;
    private NativeMenuItem? _outboundGlobalItem;
    private NativeMenuItem? _outboundDirectItem;
    private NativeMenuItem? _systemProxyItem;
    private NativeMenuItem? _tunItem;
    private NativeMenuItem? _restartCoreItem;
    private NativeMenuItem? _exitItem;
    private ServiceModeStatus _serviceModeStatus = ServiceModeStatus.Unavailable(string.Empty);
    private long _lastTrayClickTick;
    private int _isRefreshing;
    private int _refreshPending;
    private int _refreshCount;
    private bool _isDisposed;

    public async Task StartAsync()
    {
        var settings = _settingsStore.Load();
        _localization = new JsonLocalizationService(AppLanguageParser.Parse(settings.Language));
        _globalHotkeyService = GlobalHotkeyServiceFactory.Create(OnGlobalHotkeyActivated);
        _icon = TrayIconFactory.Create(_iconState);

        _showItem = new NativeMenuItem();
        _showItem.Click += OnShowClicked;

        _copyPowerShellItem = new NativeMenuItem();
        _copyPowerShellItem.Click += OnCopyPowerShellClicked;
        _copyCmdItem = new NativeMenuItem();
        _copyCmdItem.Click += OnCopyCmdClicked;
        _copyBashItem = new NativeMenuItem();
        _copyBashItem.Click += OnCopyBashClicked;
        _copyItem = new NativeMenuItem
        {
            Menu = new NativeMenu { _copyPowerShellItem, _copyCmdItem, _copyBashItem }
        };

        _outboundRuleItem = new NativeMenuItem { ToggleType = MenuItemToggleType.Radio };
        _outboundRuleItem.Click += OnOutboundRuleClicked;
        _outboundGlobalItem = new NativeMenuItem { ToggleType = MenuItemToggleType.Radio };
        _outboundGlobalItem.Click += OnOutboundGlobalClicked;
        _outboundDirectItem = new NativeMenuItem { ToggleType = MenuItemToggleType.Radio };
        _outboundDirectItem.Click += OnOutboundDirectClicked;
        _outboundItem = new NativeMenuItem
        {
            Menu = new NativeMenu { _outboundRuleItem, _outboundGlobalItem, _outboundDirectItem }
        };

        _proxyMenu = new TrayProxyMenu(_localization.GetString,
            (scope, group, node) => ExecuteRuntimeAsync("select proxy",
                token => proxyCatalog.SelectAsync(scope, group, node, token)));

        _systemProxyItem = new NativeMenuItem { ToggleType = MenuItemToggleType.CheckBox };
        _systemProxyItem.Click += OnSystemProxyClicked;
        _tunItem = new NativeMenuItem { ToggleType = MenuItemToggleType.CheckBox };
        _tunItem.Click += OnTunClicked;
        _restartCoreItem = new NativeMenuItem();
        _restartCoreItem.Click += OnRestartCoreClicked;
        _exitItem = new NativeMenuItem();
        _exitItem.Click += OnExitClicked;

        _trayIcon = new TrayIcon
        {
            Icon = _icon,
            ToolTipText = AppMetadata.Name,
            Menu = new NativeMenu
            {
                _showItem,
                _proxyMenu.Item,
                new NativeMenuItemSeparator(),
                _copyItem,
                new NativeMenuItemSeparator(),
                _outboundItem,
                new NativeMenuItemSeparator(),
                _systemProxyItem,
                _tunItem,
                _restartCoreItem,
                new NativeMenuItemSeparator(),
                _exitItem,
            },
            IsVisible = true,
        };
        _trayIcon.Clicked += OnTrayIconClicked;
        _trayIcon.ToolTipText = AppMetadata.DisplayName;
        TrayIcon.SetIcons(Avalonia.Application.Current!, [_trayIcon]);
#if DEBUG
        _debugMenu = new DebugTrayMenu(_trayIcon, GetMenuAutomationIds);
#endif

        coreRuntime.StateChanged += OnStateChanged;
        systemProxy.StatusChanged += OnSystemProxyChanged;
        _refreshTimer.Tick += OnRefreshTimerTick;
        _refreshTimer.Start();
        UpdateText();
        ScheduleRefresh();
        foreach (var (action, gesture) in new[]
        {
            (GlobalHotkeyAction.ToggleWindow, settings.WindowToggleHotkey),
            (GlobalHotkeyAction.ToggleSystemProxy, settings.SystemProxyToggleHotkey),
            (GlobalHotkeyAction.ToggleTun, settings.TunToggleHotkey),
        })
        {
            var result = await _globalHotkeyService.ApplyAsync(action, gesture).ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                AppLogger.Warning($"Global hotkey startup registration failed: action={action} error={result.Error}");
            }
        }
        AppLogger.Info("Avalonia background tray started");
    }

    public Task<GlobalHotkeyApplyResult> ApplyAsync(
        GlobalHotkeyAction action,
        string gesture,
        CancellationToken cancellationToken) =>
        Dispatcher.UIThread.InvokeAsync(
            () => RequireGlobalHotkeys().ApplyAsync(action, gesture, cancellationToken));

    public Task SetSuppressedAsync(
        Guid connectionId,
        bool isSuppressed,
        CancellationToken cancellationToken) =>
        Dispatcher.UIThread.InvokeAsync(
            () => SetSuppressedOnUiThreadAsync(connectionId, isSuppressed, cancellationToken));

    public Task ReleaseSuppressionAsync(Guid connectionId) =>
        Dispatcher.UIThread.InvokeAsync(
            () => SetSuppressedOnUiThreadAsync(connectionId, false, CancellationToken.None));

#if DEBUG
    public Task<object> ExecuteMenuDebugAsync(TrayMenuDebugRequest request, CancellationToken cancellationToken) =>
        _debugMenu!.ExecuteAsync(request, cancellationToken);

    private IReadOnlyDictionary<NativeMenuItem, string> GetMenuAutomationIds()
    {
        var ids = _proxyMenu!.GetAutomationIds();
        ids.Add(_showItem!, "Tray.ShowWindow");
        ids.Add(_copyItem!, "Tray.TerminalCommands");
        ids.Add(_copyPowerShellItem!, "Tray.Terminal.PowerShell");
        ids.Add(_copyCmdItem!, "Tray.Terminal.Cmd");
        ids.Add(_copyBashItem!, "Tray.Terminal.Bash");
        ids.Add(_outboundItem!, "Tray.OutboundMode");
        ids.Add(_outboundRuleItem!, "Tray.RuleMode");
        ids.Add(_outboundGlobalItem!, "Tray.GlobalMode");
        ids.Add(_outboundDirectItem!, "Tray.DirectMode");
        ids.Add(_systemProxyItem!, "Tray.SystemProxy");
        ids.Add(_tunItem!, "Tray.Tun");
        ids.Add(_restartCoreItem!, "Tray.RestartCore");
        ids.Add(_exitItem!, "Tray.Exit");
        return ids;
    }

    public async Task<object> GetProxyMenuAsync(CancellationToken cancellationToken)
    {
        await proxyCatalog.RefreshAsync(cancellationToken, force: true).ConfigureAwait(false);
        var snapshot = proxyCatalog.GetSnapshot();
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _proxyMenu!.Update(snapshot);
            var items = _trayIcon!.Menu!.Items;
            return _proxyMenu.Inspect(items.IndexOf(_proxyMenu.Item) == items.IndexOf(_showItem!) + 1);
        });
    }

    public async Task SelectProxyAsync(string groupName, string nodeName, CancellationToken cancellationToken)
    {
        await GetProxyMenuAsync(cancellationToken).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() => _proxyMenu!.SelectAsync(groupName, nodeName));
    }

    public Task<bool> SimulateActivationAsync(
        GlobalHotkeyAction action,
        CancellationToken cancellationToken) =>
        Dispatcher.UIThread.InvokeAsync(
            () => RequireGlobalHotkeys().SimulateActivationAsync(action, cancellationToken));
#endif

    private void OnShowClicked(object? sender, EventArgs args) =>
        _ = ExecuteAsync("show UI", token => uiSessions.ActivateAsync(token));

    private void OnTrayIconClicked(object? sender, EventArgs args)
    {
        var settings = _settingsStore.Load();
        var isRepeatedClick = ConsumeRepeatedTrayClick();
        if (settings.IsTrayDoubleClickEnabled)
        {
            if (isRepeatedClick)
            {
                _ = ToggleUiAsync();
            }
            return;
        }

        if (!isRepeatedClick)
        {
            _ = ToggleUiAsync();
        }
    }

    private bool ConsumeRepeatedTrayClick()
    {
        var current = Stopwatch.GetTimestamp();
        if (_lastTrayClickTick == 0)
        {
            _lastTrayClickTick = current;
            return false;
        }

        var elapsed = Stopwatch.GetElapsedTime(_lastTrayClickTick, current);
        _lastTrayClickTick = elapsed <= TrayDoubleClickThreshold ? 0 : current;
        return elapsed <= TrayDoubleClickThreshold;
    }

    private Task ToggleUiAsync() =>
        ExecuteAsync("toggle UI", token => uiSessions.ToggleAsync(token));

    private void OnGlobalHotkeyActivated(GlobalHotkeyAction action)
    {
        _ = action switch
        {
            GlobalHotkeyAction.ToggleWindow => ToggleUiAsync(),
            GlobalHotkeyAction.ToggleSystemProxy => ToggleSystemProxyAsync(),
            GlobalHotkeyAction.ToggleTun => ToggleTunAsync(),
            _ => Task.CompletedTask,
        };
    }

    private void OnCopyPowerShellClicked(object? sender, EventArgs args) =>
        _ = CopyTerminalProxyAsync(TrayTerminalShell.PowerShell);

    private void OnCopyCmdClicked(object? sender, EventArgs args) =>
        _ = CopyTerminalProxyAsync(TrayTerminalShell.Cmd);

    private void OnCopyBashClicked(object? sender, EventArgs args) =>
        _ = CopyTerminalProxyAsync(TrayTerminalShell.Bash);

#if DEBUG
    public Task CopyTerminalProxyAsync(CancellationToken cancellationToken) => WriteTerminalProxyAsync(
        OperatingSystem.IsWindows() ? TrayTerminalShell.PowerShell : TrayTerminalShell.Bash,
        cancellationToken);
#endif

    private Task CopyTerminalProxyAsync(TrayTerminalShell shell) =>
        ExecuteAsync("copy terminal proxy", token => WriteTerminalProxyAsync(shell, token));

    private async Task WriteTerminalProxyAsync(TrayTerminalShell shell, CancellationToken cancellationToken)
    {
        if (!IsCoreRunning())
        {
            throw new InvalidOperationException("Core is not running; terminal proxy command was not copied.");
        }

        var settings = _settingsStore.Load();
        var url = $"http://{settings.ProxyHost}:{settings.MixedPort}";
        var command = shell switch
        {
            TrayTerminalShell.PowerShell => $"$env:http_proxy=\"{url}\"; $env:https_proxy=\"{url}\"",
            TrayTerminalShell.Cmd => $"set http_proxy={url} && set https_proxy={url}",
            _ => $"export http_proxy={url} && export https_proxy={url}",
        };
        await _clipboard.WriteTextAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private void OnOutboundRuleClicked(object? sender, EventArgs args) =>
        _ = SetOutboundModeAsync(OutboundMode.Rule);

    private void OnOutboundGlobalClicked(object? sender, EventArgs args) =>
        _ = SetOutboundModeAsync(OutboundMode.Global);

    private void OnOutboundDirectClicked(object? sender, EventArgs args) =>
        _ = SetOutboundModeAsync(OutboundMode.Direct);

    private Task SetOutboundModeAsync(OutboundMode mode)
    {
        return ExecuteRuntimeAsync("set outbound mode", async token =>
        {
            if (!IsCoreRunning())
            {
                return;
            }

            if (!await _proxyClient.SetOutboundModeAsync(mode, token).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Core rejected the outbound mode change.");
            }

            var settings = _settingsStore.Load();
            settings.OutboundMode = mode.ToString();
            _settingsStore.Save(settings);
        });
    }

    private void OnSystemProxyClicked(object? sender, EventArgs args)
    {
        _ = ToggleSystemProxyAsync();
    }

    private Task ToggleSystemProxyAsync()
    {
        return ExecuteRuntimeAsync("toggle system proxy", async token =>
        {
            var status = await systemProxy.GetStatusAsync(token).ConfigureAwait(false);
            if (!status.IsEnabled && !IsCoreRunning())
            {
                return;
            }

            var settings = _settingsStore.Load();
            var request = status.IsEnabled
                ? null
                : SystemProxyApplicationRequest.Build(settings, CurrentSystemProxyPlatform());
            var result = await systemProxy.SetEnabledAsync(!status.IsEnabled, request, token).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException(result.Message);
            }
        });
    }

    private void OnTunClicked(object? sender, EventArgs args)
    {
        _ = ToggleTunAsync();
    }

    private Task ToggleTunAsync()
    {
        return ExecuteRuntimeAsync("toggle TUN", async token =>
        {
            if (!IsCoreRunning())
            {
                return;
            }

            var settings = _settingsStore.Load();
            if (!settings.IsTunEnabled && !await CanEnableTunAsync(token).ConfigureAwait(false))
            {
                return;
            }

            var previous = settings.IsTunEnabled;
            settings.IsTunEnabled = !settings.IsTunEnabled;
            _settingsStore.Save(settings);
            try
            {
                await coreRuntime.ApplyCurrentSettingsAsync(token).ConfigureAwait(false);
            }
            catch
            {
                settings.IsTunEnabled = previous;
                _settingsStore.Save(settings);
                throw;
            }
        });
    }

    private async Task SetSuppressedOnUiThreadAsync(
        Guid connectionId,
        bool isSuppressed,
        CancellationToken cancellationToken)
    {
        if (isSuppressed)
        {
            _hotkeySuppressors.Add(connectionId);
        }
        else
        {
            _hotkeySuppressors.Remove(connectionId);
        }

        await RequireGlobalHotkeys()
            .SetActivationSuppressedAsync(_hotkeySuppressors.Count > 0, cancellationToken)
            .ConfigureAwait(true);
    }

    private IGlobalHotkeyService RequireGlobalHotkeys() =>
        _globalHotkeyService ?? throw new InvalidOperationException("Tray global hotkeys are not initialized.");

    private void OnRestartCoreClicked(object? sender, EventArgs args)
    {
        _ = ExecuteRuntimeAsync("restart Core", async token =>
        {
            if (!IsCoreRunning())
            {
                return;
            }

            await coreRuntime.RestartAsync(token).ConfigureAwait(false);
            var status = await systemProxy.GetStatusAsync(token).ConfigureAwait(false);
            if (status.IsEnabled)
            {
                var settings = _settingsStore.Load();
                var result = await systemProxy.SetEnabledAsync(
                    true,
                    SystemProxyApplicationRequest.Build(settings, CurrentSystemProxyPlatform()),
                    token).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    throw new InvalidOperationException(result.Message);
                }
            }
        });
    }

    private bool IsCoreRunning() => coreRuntime.CurrentStatus.Snapshot.State == CoreState.Running;

    private async Task<bool> CanEnableTunAsync(CancellationToken cancellationToken)
    {
        if (_runMode is ProcessRunMode.Administrator or ProcessRunMode.Service)
        {
            return true;
        }

        _serviceModeStatus = await coreRuntime
            .GetServiceModeStatusAsync(cancellationToken)
            .ConfigureAwait(false);
        return _serviceModeStatus.IsRunning;
    }

    private void OnExitClicked(object? sender, EventArgs args) => lifetime.RequestStop();

    private async Task ExecuteAsync(string operation, Func<CancellationToken, Task> action)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await action(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Tray {operation} failed: {exception.Message}");
        }
        finally
        {
            ScheduleRefresh();
        }
    }

    private Task ExecuteRuntimeAsync(string operation, Func<CancellationToken, Task> action)
    {
        return ExecuteAsync(operation, async token =>
        {
            await _runtimeOperationGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await action(token).ConfigureAwait(false);
            }
            finally
            {
                _runtimeOperationGate.Release();
            }
        });
    }

    private void OnStateChanged(object? sender, object args) => ScheduleRefresh();

    private void OnSystemProxyChanged(object? sender, SystemProxyStatus status) => ScheduleRefresh();

    private void OnRefreshTimerTick(object? sender, EventArgs args) => ScheduleRefresh();

    private void ScheduleRefresh()
    {
        if (_isDisposed)
        {
            return;
        }

        Interlocked.Exchange(ref _refreshPending, 1);
        if (Interlocked.CompareExchange(ref _isRefreshing, 1, 0) == 0)
        {
            _ = RefreshStateLoopAsync();
        }
    }

    private async Task RefreshStateLoopAsync()
    {
        try
        {
            while (Interlocked.Exchange(ref _refreshPending, 0) != 0)
            {
                await RefreshStateAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            Volatile.Write(ref _isRefreshing, 0);
            if (Volatile.Read(ref _refreshPending) != 0)
            {
                ScheduleRefresh();
            }
        }
    }

    private async Task RefreshStateAsync()
    {
        try
        {
            var settings = _settingsStore.Load();
            var core = coreRuntime.CurrentStatus;
            using var proxyTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await proxyCatalog.RefreshAsync(proxyTimeout.Token).ConfigureAwait(false);
            var proxies = proxyCatalog.GetSnapshot();
            var runtime = runtimeMonitor.GetSnapshot();
            var proxy = await systemProxy.GetStatusAsync().ConfigureAwait(false);
            if (++_refreshCount == 1 || _refreshCount % 5 == 0)
            {
                _serviceModeStatus = await coreRuntime.GetServiceModeStatusAsync(CancellationToken.None).ConfigureAwait(false);
            }

            var isCoreRunning = core.Snapshot.State == CoreState.Running;
            var canToggleTun = isCoreRunning
                && (settings.IsTunEnabled
                    || _runMode is ProcessRunMode.Administrator or ProcessRunMode.Service
                    || _serviceModeStatus.IsRunning);
            var state = new TrayMenuState(
                AppLanguageParser.Parse(settings.Language),
                runtime.Mode,
                proxy.IsEnabled,
                settings.IsTunEnabled,
                isCoreRunning,
                canToggleTun,
                ResolveIconState(isCoreRunning, proxy.IsEnabled, settings.IsTunEnabled));
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ApplyState(state);
                _proxyMenu?.Update(proxies);
            });
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Tray state refresh failed: {exception.Message}");
        }
    }

    private void ApplyState(TrayMenuState state)
    {
        if (_isDisposed || _localization is null)
        {
            return;
        }

        if (_localization.CurrentLanguage != state.Language)
        {
            _localization.SetLanguage(state.Language);
            UpdateText();
        }

        if (_outboundRuleItem is not null)
        {
            _outboundRuleItem.IsChecked = state.Mode == OutboundMode.Rule;
            _outboundRuleItem.IsEnabled = state.IsCoreRunning;
        }
        if (_outboundGlobalItem is not null)
        {
            _outboundGlobalItem.IsChecked = state.Mode == OutboundMode.Global;
            _outboundGlobalItem.IsEnabled = state.IsCoreRunning;
        }
        if (_outboundDirectItem is not null)
        {
            _outboundDirectItem.IsChecked = state.Mode == OutboundMode.Direct;
            _outboundDirectItem.IsEnabled = state.IsCoreRunning;
        }
        if (_systemProxyItem is not null)
        {
            _systemProxyItem.IsChecked = state.IsSystemProxyEnabled;
            _systemProxyItem.IsEnabled = state.IsCoreRunning || state.IsSystemProxyEnabled;
        }
        if (_tunItem is not null)
        {
            _tunItem.IsChecked = state.IsTunEnabled;
            _tunItem.IsEnabled = state.CanToggleTun;
        }
        if (_restartCoreItem is not null) _restartCoreItem.IsEnabled = state.IsCoreRunning;
        if (_copyItem is not null) _copyItem.IsEnabled = state.IsCoreRunning;
        UpdateIcon(state.IconState);
    }

    private void UpdateText()
    {
        if (_localization is null)
        {
            return;
        }

        if (_showItem is not null) _showItem.Header = _localization.GetString("Tray.Show");
        if (_copyItem is not null) _copyItem.Header = _localization.GetString("Tray.CopyTerminalProxy");
        if (_copyPowerShellItem is not null) _copyPowerShellItem.Header = _localization.GetString("Tray.Terminal.PowerShell");
        if (_copyCmdItem is not null) _copyCmdItem.Header = _localization.GetString("Tray.Terminal.Cmd");
        if (_copyBashItem is not null) _copyBashItem.Header = _localization.GetString("Tray.Terminal.Bash");
        if (_outboundItem is not null) _outboundItem.Header = _localization.GetString("Tray.OutboundMode");
        if (_outboundRuleItem is not null) _outboundRuleItem.Header = _localization.GetString("Tray.RuleMode");
        if (_outboundGlobalItem is not null) _outboundGlobalItem.Header = _localization.GetString("Tray.GlobalMode");
        if (_outboundDirectItem is not null) _outboundDirectItem.Header = _localization.GetString("Tray.DirectMode");
        if (_systemProxyItem is not null) _systemProxyItem.Header = _localization.GetString("Tray.SystemProxy");
        if (_tunItem is not null) _tunItem.Header = _localization.GetString("Tray.VirtualNic");
        if (_restartCoreItem is not null) _restartCoreItem.Header = _localization.GetString("Tray.RestartCore");
        if (_exitItem is not null) _exitItem.Header = _localization.GetString("Tray.Exit");
    }

    private void UpdateIcon(TrayIconState state)
    {
        if (_trayIcon is null || _iconState == state)
        {
            return;
        }

        _iconState = state;
        _icon = TrayIconFactory.Create(state);
        _trayIcon.Icon = _icon;
    }

    private static TrayIconState ResolveIconState(bool isCoreRunning, bool isSystemProxyEnabled, bool isTunEnabled)
    {
        if (!isCoreRunning)
        {
            return TrayIconState.Disabled;
        }

        return (isSystemProxyEnabled, isTunEnabled) switch
        {
            (true, true) => TrayIconState.ProxyTunEnabled,
            (true, false) => TrayIconState.ProxyEnabled,
            (false, true) => TrayIconState.TunEnabled,
            _ => TrayIconState.Disabled,
        };
    }

    private static SystemProxyPlatform CurrentSystemProxyPlatform()
    {
        if (OperatingSystem.IsWindows()) return SystemProxyPlatform.Windows;
        if (OperatingSystem.IsLinux()) return SystemProxyPlatform.Linux;
        if (OperatingSystem.IsMacOS()) return SystemProxyPlatform.MacOS;
        return SystemProxyPlatform.Other;
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        coreRuntime.StateChanged -= OnStateChanged;
        systemProxy.StatusChanged -= OnSystemProxyChanged;
        await Dispatcher.UIThread.InvokeAsync(DisposeUi);
    }

    private void DisposeUi()
    {
        _clipboard.Dispose();
        _refreshTimer.Stop();
        _refreshTimer.Tick -= OnRefreshTimerTick;
        if (_showItem is not null) _showItem.Click -= OnShowClicked;
        if (_copyPowerShellItem is not null) _copyPowerShellItem.Click -= OnCopyPowerShellClicked;
        if (_copyCmdItem is not null) _copyCmdItem.Click -= OnCopyCmdClicked;
        if (_copyBashItem is not null) _copyBashItem.Click -= OnCopyBashClicked;
        if (_outboundRuleItem is not null) _outboundRuleItem.Click -= OnOutboundRuleClicked;
        if (_outboundGlobalItem is not null) _outboundGlobalItem.Click -= OnOutboundGlobalClicked;
        if (_outboundDirectItem is not null) _outboundDirectItem.Click -= OnOutboundDirectClicked;
        if (_systemProxyItem is not null) _systemProxyItem.Click -= OnSystemProxyClicked;
        if (_tunItem is not null) _tunItem.Click -= OnTunClicked;
        if (_restartCoreItem is not null) _restartCoreItem.Click -= OnRestartCoreClicked;
        if (_exitItem is not null) _exitItem.Click -= OnExitClicked;
        if (_trayIcon is not null)
        {
            _trayIcon.Clicked -= OnTrayIconClicked;
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
        }
        _hotkeySuppressors.Clear();
        _globalHotkeyService?.Dispose();
        _globalHotkeyService = null;
    }

    private readonly record struct TrayMenuState(
        AppLanguage Language,
        OutboundMode? Mode,
        bool IsSystemProxyEnabled,
        bool IsTunEnabled,
        bool IsCoreRunning,
        bool CanToggleTun,
        TrayIconState IconState);

    private enum TrayTerminalShell
    {
        PowerShell,
        Cmd,
        Bash,
    }
}
