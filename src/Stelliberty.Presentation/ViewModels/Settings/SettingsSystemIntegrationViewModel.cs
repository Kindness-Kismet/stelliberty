using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Localization;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Settings;
using Stelliberty.Presentation.Commands;

namespace Stelliberty.Presentation.ViewModels;

public sealed class SettingsSystemIntegrationViewModel : ViewModelBase, IDisposable
{
    private readonly AppSettings _settings;
    private readonly IAppSettingsStore _settingsStore;
    private readonly ILocalizationService _localization;
    private readonly IUwpLoopbackService? _uwpLoopbackService;
    private readonly ISystemProxyHostDetector? _systemProxyHostDetector;
    private readonly SystemProxyPlatform _systemPlatform;
    private readonly Action? _systemProxySettingsChanged;
    // 构造在 UI 线程上执行，后台枚举才能把结果发回。
    private readonly SynchronizationContext? _uiContext;
    private readonly List<string> _changeAreas = [];
    // 完整列表是事实来源；UwpLoopbackItems 只是筛选视图。
    private readonly List<UwpLoopbackItemViewModel> _allUwpItems = [];
    private bool _isUwpLoopbackDialogVisible;
    private string _uwpLoopbackStatusText = string.Empty;
    private string _uwpSearchText = string.Empty;
    private string _systemProxyBypassText = string.Empty;
    private string _pacScriptText = string.Empty;
    private IReadOnlyList<UwpLoopbackItemViewModel> _uwpLoopbackItems = [];
    private IReadOnlyList<string> _systemProxyHostCandidates = [];
    private int _uwpLoadRequestId;
    private bool _isDisposed;

    public SettingsSystemIntegrationViewModel(
        AppSettings settings,
        IAppSettingsStore settingsStore,
        ILocalizationService localization,
        IUwpLoopbackService? uwpLoopbackService,
        ISystemProxyHostDetector? systemProxyHostDetector,
        SystemProxyPlatform systemPlatform = SystemProxyPlatform.Other,
        Action? systemProxySettingsChanged = null)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _localization = localization;
        _uwpLoopbackService = uwpLoopbackService;
        _systemProxyHostDetector = systemProxyHostDetector;
        _systemPlatform = systemPlatform;
        _systemProxySettingsChanged = systemProxySettingsChanged;
        _uiContext = SynchronizationContext.Current;
        _systemProxyBypassText = CurrentSystemProxyBypass();
        _pacScriptText = CurrentPacScript();
        _localization.LanguageChanged += OnLanguageChanged;
        ShowUwpLoopbackDialogCommand = new RelayCommand(ShowUwpLoopbackDialog);
        CloseUwpLoopbackDialogCommand = new RelayCommand(CloseUwpLoopbackDialog);
        SelectAllUwpCommand = new RelayCommand(SelectAllUwp);
        InvertUwpSelectionCommand = new RelayCommand(InvertUwpSelection);
        SaveUwpLoopbackCommand = new RelayCommand(SaveUwpLoopback, CanSaveUwpLoopback);
        RefreshSystemProxyHostCandidatesCommand = new RelayCommand(RefreshSystemProxyHostCandidates);
        RestoreDefaultPacScriptCommand = new RelayCommand(RestoreDefaultPacScript);
        RestoreDefaultSystemProxyBypassCommand = new RelayCommand(RestoreDefaultSystemProxyBypass);
        RefreshSystemProxyHostCandidates();
    }

    public event EventHandler<(string Message, ToastType Type)>? ToastRequested;

    public IReadOnlyList<string> ChangeAreas => _changeAreas;

    public string SystemProxyHostText => _localization.GetString("Settings.System.ProxyHost");

    public string SystemProxyConfigText => _localization.GetString("Settings.System.ConfigTitle");

    public string SystemProxyHostDescriptionText => _localization.GetString("Settings.System.ProxyHostDescription");

    public string SystemManageUwpLoopbackButtonText => _localization.GetString("Settings.System.ManageUwpLoopback");

    public string SystemPacModeText => _localization.GetString("Settings.System.PacMode");

    public string SystemPacModeDescriptionText => _localization.GetString("Settings.System.PacModeDescription");

    public string SystemPacScriptText => _localization.GetString("Settings.System.PacScript");

    public string RestoreDefaultPacScriptText => _localization.GetString("Settings.System.RestoreDefaultPac");

    public string SystemBypassText => _localization.GetString("Settings.System.Bypass");

    public string SystemBypassDescriptionText => _systemPlatform switch
    {
        SystemProxyPlatform.Windows => _localization.GetString("Settings.System.BypassDescription.Windows"),
        SystemProxyPlatform.MacOS => _localization.GetString("Settings.System.BypassDescription.MacOS"),
        _ => _localization.GetString("Settings.System.BypassDescription.Unix")
    };

    public string RestoreDefaultBypassText => _localization.GetString("Settings.System.RestoreDefaultBypass");

    public string SystemUwpText => _localization.GetString("Settings.System.Uwp");

    public string UwpSearchWatermarkText => _localization.GetString("Settings.System.UwpLoopback.SearchWatermark");

    public string UwpSelectAllText => _localization.GetString("Settings.System.UwpLoopback.SelectAll");

    public string UwpInvertText => _localization.GetString("Settings.System.UwpLoopback.Invert");

    public string UwpSaveText => _localization.GetString("Settings.System.UwpLoopback.Save");

    public string UwpCancelText => _localization.GetString("Settings.System.UwpLoopback.Cancel");

    public string ProxyHost
    {
        get => _settings.ProxyHost;
        set
        {
            var wasUsingDefaultPacScript = string.IsNullOrWhiteSpace(_settings.PacScript);
            SetSystemIntegrationSetting(_settings.ProxyHost, value, next => _settings.ProxyHost = next);
            if (wasUsingDefaultPacScript)
            {
                _pacScriptText = DefaultPacScript();
                OnPropertyChanged(nameof(PacScript));
            }
        }
    }

    public string SystemProxyBypass
    {
        get => _systemProxyBypassText;
        set
        {
            _systemProxyBypassText = value;
            SetSystemIntegrationSetting(_settings.SystemProxyBypass, value, next => _settings.SystemProxyBypass = next);
        }
    }

    public bool IsPacModeEnabled
    {
        get => _settings.IsPacModeEnabled;
        set => SetPacModeEnabled(value);
    }

    public bool IsSystemProxyBypassVisible => !IsPacModeEnabled;

    public bool IsPacScriptVisible => IsPacModeEnabled;

    public string PacScript
    {
        get => _pacScriptText;
        set
        {
            _pacScriptText = value;
            SetSystemIntegrationSetting(_settings.PacScript, value, next => _settings.PacScript = next);
        }
    }

    public bool IsUwpLoopbackDialogVisible
    {
        get => _isUwpLoopbackDialogVisible;
        private set => SetProperty(ref _isUwpLoopbackDialogVisible, value);
    }

    public string UwpSearchText
    {
        get => _uwpSearchText;
        set
        {
            if (SetProperty(ref _uwpSearchText, value))
            {
                ApplyUwpFilter();
            }
        }
    }

    public IReadOnlyList<UwpLoopbackItemViewModel> UwpLoopbackItems
    {
        get => _uwpLoopbackItems;
        private set => SetProperty(ref _uwpLoopbackItems, value);
    }

    // Debug 入口暴露全部项目，便于命令定位包名。
    public IReadOnlyList<UwpLoopbackItemViewModel> AllUwpItems => _allUwpItems;

    public string UwpLoopbackStatusText
    {
        get => _uwpLoopbackStatusText;
        private set => SetProperty(ref _uwpLoopbackStatusText, value);
    }

    public bool IsUwpLoopbackStatusVisible => !string.IsNullOrWhiteSpace(UwpLoopbackStatusText);

    public IReadOnlyList<string> SystemProxyHostCandidates
    {
        get => _systemProxyHostCandidates;
        private set => SetProperty(ref _systemProxyHostCandidates, value);
    }

    public ICommand ShowUwpLoopbackDialogCommand { get; }

    public ICommand CloseUwpLoopbackDialogCommand { get; }

    public ICommand SelectAllUwpCommand { get; }

    public ICommand InvertUwpSelectionCommand { get; }

    public ICommand SaveUwpLoopbackCommand { get; }

    public ICommand RefreshSystemProxyHostCandidatesCommand { get; }

    public ICommand RestoreDefaultPacScriptCommand { get; }

    public ICommand RestoreDefaultSystemProxyBypassCommand { get; }

    // 自动化设置包选择，不写入系统。
    public bool SetUwpItemSelected(string packageFamilyName, bool isSelected)
    {
        var item = _allUwpItems.FirstOrDefault(item => item.PackageFamilyName == packageFamilyName);
        if (item is null)
        {
            return false;
        }

        item.IsSelected = isSelected;
        return true;
    }

    public void RefreshFromSettings()
    {
        RefreshSystemProxyHostCandidates();
        _systemProxyBypassText = CurrentSystemProxyBypass();
        _pacScriptText = CurrentPacScript();
        OnPropertyChanged(string.Empty);
        _systemProxySettingsChanged?.Invoke();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _uwpLoadRequestId++;
        _localization.LanguageChanged -= OnLanguageChanged;
        ((RelayCommand)SaveUwpLoopbackCommand).RaiseCanExecuteChanged();
    }

    private void SetPacModeEnabled(bool isEnabled)
    {
        SetSystemIntegrationSetting(_settings.IsPacModeEnabled, isEnabled, next => _settings.IsPacModeEnabled = next, nameof(IsPacModeEnabled));
        OnPropertyChanged(nameof(IsSystemProxyBypassVisible));
        OnPropertyChanged(nameof(IsPacScriptVisible));
    }

    private string DefaultSystemProxyBypass()
    {
        return SystemProxyApplicationRequest.DefaultBypassRules(_systemPlatform);
    }

    private string DefaultPacScript()
    {
        return SystemProxyApplicationRequest.DefaultPacScript(_settings.ProxyHost, _settings.MixedPort);
    }

    private string CurrentSystemProxyBypass()
    {
        return string.IsNullOrWhiteSpace(_settings.SystemProxyBypass)
            ? DefaultSystemProxyBypass()
            : _settings.SystemProxyBypass;
    }

    private string CurrentPacScript()
    {
        return string.IsNullOrWhiteSpace(_settings.PacScript) ? DefaultPacScript() : _settings.PacScript;
    }

    private void RestoreDefaultPacScript()
    {
        var wasAlreadyDefault = string.IsNullOrEmpty(_settings.PacScript);
        ForceRefreshPacScript(DefaultPacScript());
        if (wasAlreadyDefault)
        {
            return;
        }

        _settings.PacScript = string.Empty;
        SaveSystemIntegrationSetting(nameof(PacScript));
    }

    private void RestoreDefaultSystemProxyBypass()
    {
        var wasAlreadyDefault = string.IsNullOrEmpty(_settings.SystemProxyBypass);
        ForceRefreshSystemProxyBypass(DefaultSystemProxyBypass());
        if (wasAlreadyDefault)
        {
            return;
        }

        _settings.SystemProxyBypass = string.Empty;
        SaveSystemIntegrationSetting(nameof(SystemProxyBypass));
    }

    private void ForceRefreshPacScript(string value)
    {
        _pacScriptText = string.Empty;
        OnPropertyChanged(nameof(PacScript));
        _pacScriptText = value;
        OnPropertyChanged(nameof(PacScript));
    }

    private void ForceRefreshSystemProxyBypass(string value)
    {
        _systemProxyBypassText = string.Empty;
        OnPropertyChanged(nameof(SystemProxyBypass));
        _systemProxyBypassText = value;
        OnPropertyChanged(nameof(SystemProxyBypass));
    }

    private void SetSystemIntegrationSetting<T>(T currentValue, T nextValue, Action<T> assign, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(currentValue, nextValue))
        {
            return;
        }

        assign(nextValue);
        SaveSystemIntegrationSetting(propertyName);
    }

    private void SaveSystemIntegrationSetting(string? propertyName)
    {
        _settingsStore.Save(_settings);
        if (!_changeAreas.Contains("SystemIntegration", StringComparer.Ordinal))
        {
            _changeAreas.Add("SystemIntegration");
            OnPropertyChanged(nameof(ChangeAreas));
        }
        OnPropertyChanged(propertyName);
        _systemProxySettingsChanged?.Invoke();
    }

    private void ShowUwpLoopbackDialog()
    {
        // 先打开对话框，避免后台枚举阻塞入场帧。
        _allUwpItems.Clear();
        UwpSearchText = string.Empty;
        ApplyUwpFilter();
        UwpLoopbackStatusText = _localization.GetString("Settings.System.UwpLoopback.Loading");
        IsUwpLoopbackDialogVisible = true;
        OnPropertyChanged(nameof(IsUwpLoopbackStatusVisible));
        ((RelayCommand)SaveUwpLoopbackCommand).RaiseCanExecuteChanged();

        var requestId = ++_uwpLoadRequestId;
        _ = Task.Run(() =>
        {
            var packages = _uwpLoopbackService?.LoadPackages() ?? [];
            Post(() =>
            {
                if (!_isDisposed && requestId == _uwpLoadRequestId)
                {
                    ApplyLoadedUwpPackages(packages);
                }
            });
        });
    }

    private void CloseUwpLoopbackDialog()
    {
        _uwpLoadRequestId++;
        IsUwpLoopbackDialogVisible = false;
        ((RelayCommand)SaveUwpLoopbackCommand).RaiseCanExecuteChanged();
    }

    // 枚举结果回到 UI 线程；取消后直接丢弃。
    private void ApplyLoadedUwpPackages(IReadOnlyList<UwpLoopbackPackage> packages)
    {
        if (!IsUwpLoopbackDialogVisible)
        {
            return;
        }

        _allUwpItems.Clear();
        _allUwpItems.AddRange(packages.Select(package => new UwpLoopbackItemViewModel(package)));
        ApplyUwpFilter();
        UwpLoopbackStatusText = _allUwpItems.Count == 0 ? _localization.GetString("Settings.System.UwpLoopback.Empty") : string.Empty;
        OnPropertyChanged(nameof(IsUwpLoopbackStatusVisible));
        ((RelayCommand)SaveUwpLoopbackCommand).RaiseCanExecuteChanged();
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

    private void ApplyUwpFilter()
    {
        var keyword = _uwpSearchText.Trim();
        UwpLoopbackItems = string.IsNullOrEmpty(keyword)
            ? _allUwpItems.ToArray()
            : _allUwpItems.Where(item => item.Matches(keyword)).ToArray();
    }

    // 全选和反选只影响可见筛选行。
    private void SelectAllUwp()
    {
        foreach (var item in UwpLoopbackItems)
        {
            item.IsSelected = true;
        }
    }

    private void InvertUwpSelection()
    {
        foreach (var item in UwpLoopbackItems)
        {
            item.IsSelected = !item.IsSelected;
        }
    }

    private bool CanSaveUwpLoopback()
    {
        // 空的完整列表不能代表清空豁免。
        return !_isDisposed && IsUwpLoopbackDialogVisible && _allUwpItems.Count > 0;
    }

    private void SaveUwpLoopback()
    {
        // 直接执行命令不会检查 CanExecute，保存入口须再次验证。
        if (_uwpLoopbackService is null || !CanSaveUwpLoopback())
        {
            return;
        }

        var enabled = _allUwpItems.Where(item => item.IsSelected).Select(item => item.PackageFamilyName).ToArray();
        var result = _uwpLoopbackService.SetLoopbackBatch(enabled);
        if (result.IsSuccess)
        {
            AppLogger.Info($"UWP loopback save succeeded: {result.Message}");
            UwpLoopbackStatusText = string.Empty;
            ToastRequested?.Invoke(this, (_localization.GetString("Settings.System.UwpLoopback.Toast.Saved"), ToastType.Success));
        }
        else
        {
            AppLogger.Warning($"UWP loopback save failed: {result.Message}");
            UwpLoopbackStatusText = string.Empty;
            ToastRequested?.Invoke(this, (_localization.GetString("Settings.System.UwpLoopback.Toast.AdminRequired"), ToastType.Error));
        }

        OnPropertyChanged(nameof(IsUwpLoopbackStatusVisible));
    }

    private void RefreshSystemProxyHostCandidates()
    {
        var detection = _systemProxyHostDetector?.Detect() ?? new SystemProxyHostDetectionResult(null, []);
        var candidates = Stelliberty.Application.Platform.SystemProxyHostCandidates
            .Build(detection.HostName, detection.NetworkAddresses)
            .ToList();
        // 当前主机缺失时合并进去，让只读下拉框能显示选择。
        if (!string.IsNullOrWhiteSpace(_settings.ProxyHost)
            && !candidates.Contains(_settings.ProxyHost, StringComparer.OrdinalIgnoreCase))
        {
            candidates.Insert(0, _settings.ProxyHost);
        }

        SystemProxyHostCandidates = candidates;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(SystemProxyHostText));
        OnPropertyChanged(nameof(SystemProxyConfigText));
        OnPropertyChanged(nameof(SystemProxyHostDescriptionText));
        OnPropertyChanged(nameof(SystemManageUwpLoopbackButtonText));
        OnPropertyChanged(nameof(SystemPacModeText));
        OnPropertyChanged(nameof(SystemPacModeDescriptionText));
        OnPropertyChanged(nameof(SystemPacScriptText));
        OnPropertyChanged(nameof(RestoreDefaultPacScriptText));
        OnPropertyChanged(nameof(RestoreDefaultBypassText));
        OnPropertyChanged(nameof(SystemBypassText));
        OnPropertyChanged(nameof(SystemBypassDescriptionText));
        OnPropertyChanged(nameof(SystemUwpText));
        OnPropertyChanged(nameof(UwpSearchWatermarkText));
        OnPropertyChanged(nameof(UwpSelectAllText));
        OnPropertyChanged(nameof(UwpInvertText));
        OnPropertyChanged(nameof(UwpSaveText));
        OnPropertyChanged(nameof(UwpCancelText));
    }
}
