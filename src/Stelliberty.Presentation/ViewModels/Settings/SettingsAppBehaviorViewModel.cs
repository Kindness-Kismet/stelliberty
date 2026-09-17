using System.Runtime.CompilerServices;
using System.Windows.Input;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Localization;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Settings;
using Stelliberty.Presentation.Commands;

namespace Stelliberty.Presentation.ViewModels;

public sealed class SettingsAppBehaviorViewModel : ViewModelBase, IDisposable
{
    private readonly AppSettings _settings;
    private readonly IAppSettingsStore _settingsStore;
    private readonly ILocalizationService _localization;
    private readonly IAppBehaviorService _service;
    private readonly IGlobalHotkeyService _globalHotkeyService;

    public SettingsAppBehaviorViewModel(
        AppSettings settings,
        IAppSettingsStore settingsStore,
        ILocalizationService localization,
        IAppBehaviorService service,
        IGlobalHotkeyService globalHotkeyService)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _localization = localization;
        _service = service;
        _globalHotkeyService = globalHotkeyService;
        ToggleAutoStartCommand = new RelayCommand(() => SetAutoStartEnabled(!IsAutoStartEnabled));
        ClearWindowToggleHotkeyCommand = new RelayCommand(
            () => SetWindowToggleHotkeyAsync(string.Empty).SafeFireAndForget("ClearWindowToggleHotkey"));
        ClearSystemProxyToggleHotkeyCommand = new RelayCommand(
            () => SetSystemProxyToggleHotkeyAsync(string.Empty).SafeFireAndForget("ClearSystemProxyToggleHotkey"));
        ClearTunToggleHotkeyCommand = new RelayCommand(
            () => SetTunToggleHotkeyAsync(string.Empty).SafeFireAndForget("ClearTunToggleHotkey"));
        _localization.LanguageChanged += OnLanguageChanged;
    }

    public event EventHandler<(string Message, ToastType Type)>? ToastRequested;

    public string SilentStartText => _localization.GetString("Settings.AppBehavior.SilentStart");

    public string SilentStartDescriptionText => _localization.GetString("Settings.AppBehavior.SilentStart.Description");

    public string MinimizeToTrayText => _localization.GetString("Settings.AppBehavior.MinimizeToTray");

    public string LightweightModeText => _localization.GetString("Settings.AppBehavior.LightweightMode");

    public string LightweightModeDescriptionText => _localization.GetString("Settings.AppBehavior.LightweightMode.Description");

    public string TrayDoubleClickText => _localization.GetString("Settings.AppBehavior.TrayDoubleClick");

    public string TrayDoubleClickDescriptionText => _localization.GetString("Settings.AppBehavior.TrayDoubleClick.Description");

    public string LazyModeText => _localization.GetString("Settings.AppBehavior.LazyMode");

    public string LazyModeDescriptionText => _localization.GetString("Settings.AppBehavior.LazyMode.Description");

    public string TitleBarFpsText => _localization.GetString("Settings.AppBehavior.TitleBarFps");

    public string StartupText => _localization.GetString("Settings.AppBehavior.Startup");

    public string HotkeysText => _localization.GetString("Settings.AppBehavior.Hotkeys");

    public string WindowToggleHotkeyText => _localization.GetString("Settings.AppBehavior.WindowToggleHotkey");

    public string SystemProxyToggleHotkeyText => _localization.GetString("Settings.AppBehavior.SystemProxyToggleHotkey");

    public string TunToggleHotkeyText => _localization.GetString("Settings.AppBehavior.TunToggleHotkey");

    public string HotkeyWatermarkText => _localization.GetString("Settings.AppBehavior.Hotkey.Watermark");

    public string ClearHotkeyText => _localization.GetString("Settings.AppBehavior.Hotkey.Clear");

    public IReadOnlyList<string> Items =>
    [
        SilentStartText,
        MinimizeToTrayText,
        LightweightModeText,
        TrayDoubleClickText,
        LazyModeText,
        TitleBarFpsText,
        StartupText,
        WindowToggleHotkeyText,
        SystemProxyToggleHotkeyText,
        TunToggleHotkeyText,
    ];

    public bool IsSilentStartEnabled
    {
        get => _settings.IsSilentStartEnabled;
        set => SetSilentStartEnabled(value);
    }

    public bool IsMinimizeToTrayEnabled
    {
        get => _settings.IsMinimizeToTrayEnabled;
        set => Apply(_settings.IsMinimizeToTrayEnabled, value, next => _settings.IsMinimizeToTrayEnabled = next);
    }

    public bool IsLightweightModeEnabled
    {
        get => _settings.IsLightweightModeEnabled;
        set => Apply(_settings.IsLightweightModeEnabled, value, next => _settings.IsLightweightModeEnabled = next);
    }

    public bool IsLazyModeEnabled
    {
        get => _settings.IsLazyModeEnabled;
        set => Apply(_settings.IsLazyModeEnabled, value, next => _settings.IsLazyModeEnabled = next);
    }

    public bool IsTrayDoubleClickEnabled
    {
        get => _settings.IsTrayDoubleClickEnabled;
        set => Apply(_settings.IsTrayDoubleClickEnabled, value, next => _settings.IsTrayDoubleClickEnabled = next);
    }

    public bool IsTitleBarFpsVisible
    {
        get => _settings.IsTitleBarFpsVisible;
        set => Apply(_settings.IsTitleBarFpsVisible, value, next => _settings.IsTitleBarFpsVisible = next);
    }

    public bool IsAutoStartEnabled => _settings.IsAutoStartEnabled;

    public string WindowToggleHotkey => _settings.WindowToggleHotkey;

    public string SystemProxyToggleHotkey => _settings.SystemProxyToggleHotkey;

    public string TunToggleHotkey => _settings.TunToggleHotkey;

    public ICommand ToggleAutoStartCommand { get; }

    public ICommand ClearWindowToggleHotkeyCommand { get; }

    public ICommand ClearSystemProxyToggleHotkeyCommand { get; }

    public ICommand ClearTunToggleHotkeyCommand { get; }

    public void SetAutoStartEnabled(bool isEnabled)
    {
        if (_settings.IsAutoStartEnabled == isEnabled)
        {
            return;
        }

        try
        {
            _service.Apply(BuildRequest(isEnabled));
            _settings.IsAutoStartEnabled = isEnabled;
            _settingsStore.Save(_settings);
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"App behavior apply failed: {exception.Message}");
        }

        OnPropertyChanged(nameof(IsAutoStartEnabled));
    }

    private void SetSilentStartEnabled(bool isEnabled)
    {
        if (_settings.IsSilentStartEnabled == isEnabled)
        {
            return;
        }

        var previous = _settings.IsSilentStartEnabled;
        _settings.IsSilentStartEnabled = isEnabled;
        try
        {
            if (_settings.IsAutoStartEnabled)
            {
                _service.Apply(BuildRequest(isAutoStartEnabled: true));
            }
            _settingsStore.Save(_settings);
        }
        catch (Exception exception)
        {
            _settings.IsSilentStartEnabled = previous;
            AppLogger.Warning($"App behavior apply failed: {exception.Message}");
        }

        OnPropertyChanged(nameof(IsSilentStartEnabled));
    }

    public void RefreshFromSettings()
    {
        OnPropertyChanged(string.Empty);
    }

    public Task SetWindowToggleHotkeyAsync(string gesture)
    {
        return SetHotkeyAsync(
            GlobalHotkeyAction.ToggleWindow,
            gesture,
            _settings.WindowToggleHotkey,
            value => _settings.WindowToggleHotkey = value,
            nameof(WindowToggleHotkey));
    }

    public Task SetSystemProxyToggleHotkeyAsync(string gesture)
    {
        return SetHotkeyAsync(
            GlobalHotkeyAction.ToggleSystemProxy,
            gesture,
            _settings.SystemProxyToggleHotkey,
            value => _settings.SystemProxyToggleHotkey = value,
            nameof(SystemProxyToggleHotkey));
    }

    public Task SetTunToggleHotkeyAsync(string gesture)
    {
        return SetHotkeyAsync(
            GlobalHotkeyAction.ToggleTun,
            gesture,
            _settings.TunToggleHotkey,
            value => _settings.TunToggleHotkey = value,
            nameof(TunToggleHotkey));
    }

    public void SetHotkeyCaptureActive(bool isActive)
    {
        SetHotkeyCaptureActiveAsync(isActive).SafeFireAndForget("SetHotkeyCaptureActive");
    }

#if DEBUG
    public Task<bool> SimulateHotkeyActivationAsync(GlobalHotkeyAction action)
    {
        return _globalHotkeyService.SimulateActivationAsync(action);
    }
#endif

    private async Task SetHotkeyAsync(
        GlobalHotkeyAction action,
        string gesture,
        string currentValue,
        Action<string> assign,
        string propertyName)
    {
        var nextValue = gesture.Trim();
        GlobalHotkeyApplyResult result;
        try
        {
            result = await _globalHotkeyService.ApplyAsync(action, nextValue).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Global hotkey apply failed: action={action} error={exception.Message}");
            ShowHotkeyResult(GlobalHotkeyApplyResult.Failure(GlobalHotkeyApplyError.Failed), nextValue);
            return;
        }
        if (!result.IsSuccess)
        {
            AppLogger.Warning($"Global hotkey apply failed: action={action} error={result.Error}");
            ShowHotkeyResult(result, nextValue);
            return;
        }

        if (string.Equals(currentValue, nextValue, StringComparison.Ordinal))
        {
            ShowHotkeyResult(result, nextValue);
            return;
        }

        assign(nextValue);
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception exception)
        {
            GlobalHotkeyApplyResult restoreResult;
            try
            {
                restoreResult = await _globalHotkeyService.ApplyAsync(action, currentValue).ConfigureAwait(true);
            }
            catch (Exception restoreException)
            {
                restoreResult = GlobalHotkeyApplyResult.Failure(GlobalHotkeyApplyError.Failed);
                AppLogger.Warning($"Global hotkey restore failed: action={action} error={restoreException.Message}");
            }
            assign(currentValue);
            AppLogger.Warning($"Global hotkey save failed: action={action} error={exception.Message}");
            if (!restoreResult.IsSuccess)
            {
                AppLogger.Warning($"Global hotkey restore failed: action={action} error={restoreResult.Error}");
            }

            ToastRequested?.Invoke(
                this,
                (_localization.GetString("Settings.AppBehavior.Hotkey.Toast.SaveFailed"), ToastType.Error));
            OnPropertyChanged(propertyName);
            return;
        }

        OnPropertyChanged(propertyName);
        ShowHotkeyResult(result, nextValue);
    }

    private async Task SetHotkeyCaptureActiveAsync(bool isActive)
    {
        try
        {
            await _globalHotkeyService.SetActivationSuppressedAsync(isActive).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Global hotkey suppression failed: {exception.Message}");
        }
    }

    public void Dispose()
    {
        _localization.LanguageChanged -= OnLanguageChanged;
    }

    private void Apply<T>(T currentValue, T nextValue, Action<T> assign, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(currentValue, nextValue))
        {
            return;
        }

        assign(nextValue);
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception exception)
        {
            assign(currentValue);
            AppLogger.Warning($"App behavior apply failed: {exception.Message}");
        }

        OnPropertyChanged(propertyName);
    }

    private AppBehaviorApplicationRequest BuildRequest(bool isAutoStartEnabled) => new(
        _settings.IsSilentStartEnabled,
        isAutoStartEnabled);

    private string HotkeyErrorText(GlobalHotkeyApplyError error)
    {
        var key = error switch
        {
            GlobalHotkeyApplyError.Invalid => "Settings.AppBehavior.Hotkey.Toast.Invalid",
            GlobalHotkeyApplyError.Duplicate => "Settings.AppBehavior.Hotkey.Toast.Duplicate",
            GlobalHotkeyApplyError.Conflict => "Settings.AppBehavior.Hotkey.Toast.Conflict",
            GlobalHotkeyApplyError.Unsupported => "Settings.AppBehavior.Hotkey.Toast.Unsupported",
            _ => "Settings.AppBehavior.Hotkey.Toast.Failed",
        };
        return _localization.GetString(key);
    }

    private void ShowHotkeyResult(GlobalHotkeyApplyResult result, string gesture)
    {
        if (!result.IsSuccess)
        {
            ToastRequested?.Invoke(this, (HotkeyErrorText(result.Error), ToastType.Error));
            return;
        }

        var key = string.IsNullOrEmpty(gesture)
            ? "Settings.AppBehavior.Hotkey.Toast.Cleared"
            : "Settings.AppBehavior.Hotkey.Toast.Registered";
        ToastRequested?.Invoke(this, (_localization.GetString(key), ToastType.Success));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(SilentStartText));
        OnPropertyChanged(nameof(SilentStartDescriptionText));
        OnPropertyChanged(nameof(MinimizeToTrayText));
        OnPropertyChanged(nameof(TrayDoubleClickText));
        OnPropertyChanged(nameof(TrayDoubleClickDescriptionText));
        OnPropertyChanged(nameof(LazyModeText));
        OnPropertyChanged(nameof(LazyModeDescriptionText));
        OnPropertyChanged(nameof(TitleBarFpsText));
        OnPropertyChanged(nameof(StartupText));
        OnPropertyChanged(nameof(HotkeysText));
        OnPropertyChanged(nameof(WindowToggleHotkeyText));
        OnPropertyChanged(nameof(SystemProxyToggleHotkeyText));
        OnPropertyChanged(nameof(TunToggleHotkeyText));
        OnPropertyChanged(nameof(HotkeyWatermarkText));
        OnPropertyChanged(nameof(ClearHotkeyText));
        OnPropertyChanged(nameof(Items));
    }

}
