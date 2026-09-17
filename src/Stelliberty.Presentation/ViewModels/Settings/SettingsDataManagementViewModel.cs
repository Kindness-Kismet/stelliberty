using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Input;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Localization;
using Stelliberty.Application.Settings;
using Stelliberty.Presentation.Commands;

namespace Stelliberty.Presentation.ViewModels;

public sealed class SettingsDataManagementViewModel : ViewModelBase, IDisposable
{
    private readonly DialogCloseResetScheduler _restoreCloseReset = new();
    // 与其它加载反馈保持一致，避免过快闪烁。
    private static readonly TimeSpan PrimaryWebDavBusyDuration = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan DeleteWebDavBusyDuration = TimeSpan.FromMilliseconds(300);
    private readonly IDataManagementService? _service;
    private readonly IWebDavDataBackupService? _webDavService;
    private readonly ILocalizationService _localization;
    private readonly AppSettings _settings;
    private readonly IAppSettingsStore? _settingsStore;
    private readonly Func<DateTimeOffset> _now;
    private string _lastOperation = string.Empty;
    private bool _isRestoreDialogVisible;
    private DataRestoreMode _selectedRestoreMode = DataRestoreMode.Overwrite;
    private string _pendingBackupPath = string.Empty;
    private bool _isPendingWebDavRestore;
    private bool _isWebDavBusy;
    private bool _isCreatingWebDavBackup;
    private bool _isTestingWebDavConnection;
    private bool _isLoadingWebDavBackups;
    private bool _isWebDavBackupDialogVisible;
    private string _webDavStatusText = string.Empty;
    private string _webDavBackupCacheKey = string.Empty;
    private IReadOnlyList<RemoteBackupEntry> _cachedWebDavBackupEntries = [];
    private readonly ObservableCollection<WebDavBackupItemViewModel> _webDavBackupItems = [];

    public event EventHandler<DataRestoreMode>? RestoreCompleted;
    public event EventHandler<(string Message, ToastType Type)>? ToastRequested;

    public SettingsDataManagementViewModel(
        IDataManagementService? service,
        ILocalizationService localization,
        AppSettings? settings = null,
        IAppSettingsStore? settingsStore = null,
        IWebDavDataBackupService? webDavService = null,
        Func<DateTimeOffset>? now = null)
    {
        _service = service;
        _webDavService = webDavService;
        _localization = localization;
        _settings = settings ?? new AppSettings();
        _settingsStore = settingsStore;
        _now = now ?? (() => DateTimeOffset.Now);
        _localization.LanguageChanged += OnLanguageChanged;
        CreateBackupCommand = new RelayCommand(CreateBackup);
        ShowRestoreLatestDialogCommand = new RelayCommand(OpenRestoreDialog);
        SelectOverwriteModeCommand = new RelayCommand(() => SelectedRestoreMode = DataRestoreMode.Overwrite);
        SelectMergeModeCommand = new RelayCommand(() => SelectedRestoreMode = DataRestoreMode.Merge);
        ConfirmRestoreCommand = new RelayCommand(() => _ = ConfirmRestoreAsync());
        CancelRestoreCommand = new RelayCommand(CancelRestore);
        TestWebDavConnectionCommand = new RelayCommand(() => _ = TestWebDavConnectionAsync());
        CreateWebDavBackupCommand = new RelayCommand(() => _ = CreateWebDavBackupAsync());
        ShowWebDavRestoreLatestDialogCommand = new RelayCommand(BeginWebDavRestoreLatest);
        OpenWebDavBackupDialogCommand = new RelayCommand(() => _ = OpenWebDavBackupDialogAsync());
        CloseWebDavBackupDialogCommand = new RelayCommand(CloseWebDavBackupDialog);
        RefreshWebDavBackupsCommand = new RelayCommand(() => _ = RefreshWebDavBackupsAsync());
        RestoreWebDavBackupCommand = new RelayCommand<string>(fileName => _ = RestoreWebDavBackupAsync(fileName));
        DeleteWebDavBackupCommand = new RelayCommand<string>(fileName => _ = DeleteWebDavBackupAsync(fileName));
    }

    public string BackupText => _localization.GetString("Settings.Data.Backup");

    public string RestoreText => _localization.GetString("Settings.Data.Restore");

    public string DescriptionText => _localization.GetString("Settings.Data.Description");

    public string BackupDescriptionText => _localization.GetString("Settings.Data.Backup.Description");

    public string RestoreDescriptionText => _localization.GetString("Settings.Data.Restore.Description");

    public string WebDavGroupText => _localization.GetString("Settings.Data.WebDav.Group");

    public string WebDavEnableText => _localization.GetString("Settings.Data.WebDav.Enable");

    public string WebDavUrlText => _localization.GetString("Settings.Data.WebDav.Url");

    public string WebDavUserNameText => _localization.GetString("Settings.Data.WebDav.UserName");

    public string WebDavPasswordText => _localization.GetString("Settings.Data.WebDav.Password");

    public string WebDavRemoteDirectoryText => _localization.GetString("Settings.Data.WebDav.RemoteDirectory");

    public string WebDavIntervalText => _localization.GetString("Settings.Data.WebDav.Interval");

    public string WebDavRetentionText => _localization.GetString("Settings.Data.WebDav.Retention");

    public string WebDavTestText => _localization.GetString("Settings.Data.WebDav.Test");

    public string WebDavBackupText => _localization.GetString("Settings.Data.WebDav.Backup");

    public string WebDavRestoreText => _localization.GetString("Settings.Data.WebDav.Restore");

    public string WebDavDeleteText => _localization.GetString("Common.Delete");

    public string WebDavManageText => _localization.GetString("Settings.Data.WebDav.Manage");

    public string WebDavBackupDialogTitleText => _localization.GetString("Settings.Data.WebDav.Dialog.Title");

    public string RestoreDialogTitleText => _localization.GetString("Settings.Data.RestoreDialog.Title");

    public string RestoreModeOverwriteText => _localization.GetString("Settings.Data.RestoreMode.Overwrite");

    public string RestoreModeOverwriteDescriptionText => _localization.GetString("Settings.Data.RestoreMode.Overwrite.Description");

    public string RestoreModeMergeText => _localization.GetString("Settings.Data.RestoreMode.Merge");

    public string RestoreModeMergeDescriptionText => _localization.GetString("Settings.Data.RestoreMode.Merge.Description");

    public string RestoreConfirmText => _localization.GetString("Common.Confirm");

    public string RestoreCancelText => _localization.GetString("Common.Cancel");

    public string RestoreTargetText => string.IsNullOrWhiteSpace(_pendingBackupPath)
        ? _localization.GetString(_isPendingWebDavRestore ? "Settings.Data.RestoreDialog.WebDavLatestTarget" : "Settings.Data.RestoreDialog.LatestTarget")
        : Path.GetFileName(_pendingBackupPath);

    public string LastOperation
    {
        get => _lastOperation;
        private set => SetProperty(ref _lastOperation, value);
    }

    public bool IsRestoreDialogVisible
    {
        get => _isRestoreDialogVisible;
        private set => SetProperty(ref _isRestoreDialogVisible, value);
    }

    public DataRestoreMode SelectedRestoreMode
    {
        get => _selectedRestoreMode;
        private set
        {
            if (SetProperty(ref _selectedRestoreMode, value))
            {
                OnPropertyChanged(nameof(IsOverwriteSelected));
                OnPropertyChanged(nameof(IsMergeSelected));
            }
        }
    }

    public bool IsOverwriteSelected => SelectedRestoreMode == DataRestoreMode.Overwrite;

    public bool IsMergeSelected => SelectedRestoreMode == DataRestoreMode.Merge;

    public bool IsWebDavBackupEnabled
    {
        get => _settings.IsWebDavBackupEnabled;
        set => SetSetting(_settings.IsWebDavBackupEnabled, value, next => _settings.IsWebDavBackupEnabled = next);
    }

    public string WebDavUrl
    {
        get => _settings.WebDavUrl;
        set => SetSetting(_settings.WebDavUrl, value, next => _settings.WebDavUrl = next);
    }

    public string WebDavUserName
    {
        get => _settings.WebDavUserName;
        set => SetSetting(_settings.WebDavUserName, value, next => _settings.WebDavUserName = next);
    }

    public string WebDavPassword
    {
        get => _settings.WebDavPassword;
        set => SetSetting(_settings.WebDavPassword, value, next => _settings.WebDavPassword = next);
    }

    public string WebDavRemoteDirectory
    {
        get => _settings.WebDavRemoteDirectory;
        set => SetSetting(_settings.WebDavRemoteDirectory, value, next => _settings.WebDavRemoteDirectory = next);
    }

    public IReadOnlyList<SelectionOption<int>> WebDavBackupIntervalOptions =>
    [
        new(1, FormatHourOption(1)),
        new(3, FormatHourOption(3)),
        new(6, FormatHourOption(6)),
        new(12, FormatHourOption(12)),
        new(24, FormatHourOption(24)),
    ];

    public SelectionOption<int> SelectedWebDavBackupIntervalOption
    {
        get => WebDavBackupIntervalOptions.FirstOrDefault(option => option.Value == _settings.WebDavBackupIntervalHours)
            ?? WebDavBackupIntervalOptions[^1];
        set
        {
            SetSetting(_settings.WebDavBackupIntervalHours, value.Value, next => _settings.WebDavBackupIntervalHours = next);
            OnPropertyChanged(nameof(WebDavBackupIntervalHoursText));
        }
    }

    public string WebDavBackupIntervalHoursText
    {
        get => _settings.WebDavBackupIntervalHours.ToString();
        set => SetWebDavOptionValue(
            value,
            WebDavBackupIntervalOptions,
            _settings.WebDavBackupIntervalHours,
            next => _settings.WebDavBackupIntervalHours = next,
            nameof(SelectedWebDavBackupIntervalOption),
            nameof(WebDavBackupIntervalHoursText));
    }

    public IReadOnlyList<SelectionOption<int>> WebDavBackupRetentionOptions =>
    [
        new(1, FormatCountOption(1)),
        new(2, FormatCountOption(2)),
        new(3, FormatCountOption(3)),
        new(4, FormatCountOption(4)),
        new(5, FormatCountOption(5)),
    ];

    public SelectionOption<int> SelectedWebDavBackupRetentionOption
    {
        get => WebDavBackupRetentionOptions.FirstOrDefault(option => option.Value == _settings.WebDavBackupRetentionCount)
            ?? WebDavBackupRetentionOptions[^1];
        set
        {
            SetSetting(_settings.WebDavBackupRetentionCount, value.Value, next => _settings.WebDavBackupRetentionCount = next);
            OnPropertyChanged(nameof(WebDavBackupRetentionCountText));
        }
    }

    public string WebDavBackupRetentionCountText
    {
        get => _settings.WebDavBackupRetentionCount.ToString();
        set => SetWebDavOptionValue(
            value,
            WebDavBackupRetentionOptions,
            _settings.WebDavBackupRetentionCount,
            next => _settings.WebDavBackupRetentionCount = next,
            nameof(SelectedWebDavBackupRetentionOption),
            nameof(WebDavBackupRetentionCountText));
    }

    public bool IsWebDavBusy
    {
        get => _isWebDavBusy;
        private set
        {
            if (SetProperty(ref _isWebDavBusy, value))
            {
                OnPropertyChanged(nameof(CanRunWebDavOperations));
                OnPropertyChanged(nameof(CanManageWebDavBackups));
                OnPropertyChanged(nameof(IsWebDavBackupDialogBusy));
                RefreshWebDavBackupItemLocks();
            }
        }
    }

    public bool CanRunWebDavOperations =>
        _webDavService is not null
        && !IsWebDavBusy
        && !IsCreatingWebDavBackup
        && !IsTestingWebDavConnection
        && !IsLoadingWebDavBackups
        && !HasBusyWebDavBackupItem;

    public bool CanManageWebDavBackups =>
        CanRunWebDavOperations
        && IsWebDavBackupEnabled
        && !string.IsNullOrWhiteSpace(WebDavUrl)
        && !string.IsNullOrWhiteSpace(WebDavUserName)
        && !string.IsNullOrWhiteSpace(WebDavPassword);

    public bool IsCreatingWebDavBackup
    {
        get => _isCreatingWebDavBackup;
        private set
        {
            if (SetProperty(ref _isCreatingWebDavBackup, value))
            {
                OnPropertyChanged(nameof(CanRunWebDavOperations));
                RefreshWebDavBackupDialogState();
            }
        }
    }

    public bool IsTestingWebDavConnection
    {
        get => _isTestingWebDavConnection;
        private set
        {
            if (SetProperty(ref _isTestingWebDavConnection, value))
            {
                OnPropertyChanged(nameof(CanRunWebDavOperations));
                RefreshWebDavBackupDialogState();
            }
        }
    }

    public bool IsLoadingWebDavBackups
    {
        get => _isLoadingWebDavBackups;
        private set
        {
            if (SetProperty(ref _isLoadingWebDavBackups, value))
            {
                OnPropertyChanged(nameof(CanRunWebDavOperations));
                RefreshWebDavBackupDialogState();
            }
        }
    }

    public bool IsWebDavBackupDialogBusy =>
        IsWebDavBusy || IsLoadingWebDavBackups || IsCreatingWebDavBackup || IsTestingWebDavConnection || HasBusyWebDavBackupItem;

    public bool IsWebDavBackupDialogVisible
    {
        get => _isWebDavBackupDialogVisible;
        private set => SetProperty(ref _isWebDavBackupDialogVisible, value);
    }

    public ObservableCollection<WebDavBackupItemViewModel> WebDavBackupItems => _webDavBackupItems;

    private bool HasBusyWebDavBackupItem => WebDavBackupItems.Any(item => item.IsBusy);

    public string WebDavStatusText
    {
        get => _webDavStatusText;
        private set
        {
            if (SetProperty(ref _webDavStatusText, value))
            {
                OnPropertyChanged(nameof(IsWebDavStatusVisible));
            }
        }
    }

    public bool IsWebDavStatusVisible => !string.IsNullOrWhiteSpace(WebDavStatusText);

    public IReadOnlyList<string> Items =>
    [
        _localization.GetString("Settings.Data.Backup"),
        _localization.GetString("Settings.Data.Restore"),
        _localization.GetString("Settings.Data.WebDav.Group"),
    ];

    public ICommand CreateBackupCommand { get; }

    public ICommand ShowRestoreLatestDialogCommand { get; }

    public ICommand SelectOverwriteModeCommand { get; }

    public ICommand SelectMergeModeCommand { get; }

    public ICommand ConfirmRestoreCommand { get; }

    public ICommand CancelRestoreCommand { get; }

    public ICommand TestWebDavConnectionCommand { get; }

    public ICommand CreateWebDavBackupCommand { get; }

    public ICommand ShowWebDavRestoreLatestDialogCommand { get; }

    public ICommand OpenWebDavBackupDialogCommand { get; }

    public ICommand CloseWebDavBackupDialogCommand { get; }

    public ICommand RefreshWebDavBackupsCommand { get; }

    public ICommand RestoreWebDavBackupCommand { get; }

    public ICommand DeleteWebDavBackupCommand { get; }

    public void RefreshFromSettings()
    {
        OnPropertyChanged(string.Empty);
    }

    public void Dispose()
    {
        _restoreCloseReset.Cancel();
        _localization.LanguageChanged -= OnLanguageChanged;
    }

    private void OpenRestoreDialog()
    {
        ShowRestoreDialog(string.Empty, isWebDavRestore: false);
    }

    public void BeginRestoreFromFile(string backupPath)
    {
        if (string.IsNullOrWhiteSpace(backupPath))
        {
            return;
        }

        ShowRestoreDialog(backupPath, isWebDavRestore: false);
    }

    public void CreateBackupToFile(string backupPath)
    {
        if (string.IsNullOrWhiteSpace(backupPath))
        {
            return;
        }

        LastOperation = "Backup";
        Apply(() => _service?.CreateBackup(backupPath), _localization.GetString("Settings.Data.Toast.BackupCreated"));
    }

    private void CreateBackup()
    {
        LastOperation = "Backup";
        Apply(() => _service?.CreateBackup(), _localization.GetString("Settings.Data.Toast.BackupCreated"));
    }

    private void BeginWebDavRestoreLatest()
    {
        ShowRestoreDialog(string.Empty, isWebDavRestore: true);
    }

    private void ShowRestoreDialog(string backupPath, bool isWebDavRestore)
    {
        _restoreCloseReset.Cancel();
        _pendingBackupPath = backupPath;
        _isPendingWebDavRestore = isWebDavRestore;
        SelectedRestoreMode = DataRestoreMode.Overwrite;
        OnPropertyChanged(nameof(RestoreTargetText));
        IsRestoreDialogVisible = true;
    }

    public async Task TestWebDavConnectionAsync()
    {
        if (!CanManageWebDavBackups)
        {
            return;
        }

        LastOperation = "WebDavTest";
        IsTestingWebDavConnection = true;
        try
        {
            await ApplyWebDavAsync(
                settings => _webDavService!.TestConnectionAsync(settings, CancellationToken.None),
                _localization.GetString("Settings.Data.WebDav.Toast.TestSucceeded"),
                PrimaryWebDavBusyDuration);
        }
        finally
        {
            IsTestingWebDavConnection = false;
        }
    }

    public async Task OpenWebDavBackupDialogAsync()
    {
        if (!CanManageWebDavBackups)
        {
            return;
        }

        EnsureWebDavBackupCacheScope();
        IsWebDavBackupDialogVisible = true;
        await RefreshWebDavBackupsAsync();
    }

    public void CloseWebDavBackupDialog()
    {
        if (IsWebDavBackupDialogBusy)
        {
            return;
        }

        IsWebDavBackupDialogVisible = false;
    }

    public async Task RefreshWebDavBackupsAsync()
    {
        if (!CanRunWebDavOperations)
        {
            return;
        }

        var webDavService = _webDavService;
        if (webDavService is null)
        {
            return;
        }

        var settings = CurrentWebDavSettings();
        var cacheKey = WebDavBackupCacheKey(settings);
        if (!string.Equals(_webDavBackupCacheKey, cacheKey, StringComparison.Ordinal))
        {
            _webDavBackupCacheKey = cacheKey;
            _cachedWebDavBackupEntries = [];
            WebDavBackupItems.Clear();
        }

        IsLoadingWebDavBackups = true;
        try
        {
            var entries = await webDavService.ListBackupsAsync(settings, CancellationToken.None);
            UpdateWebDavBackupItems(entries);
            WebDavStatusText = string.Empty;
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"WebDAV backup list failed: {exception.Message}");
            WebDavStatusText = _localization.GetString("Settings.Data.WebDav.Toast.ListFailed");
            ToastRequested?.Invoke(this, (WebDavStatusText, ToastType.Error));
        }
        finally
        {
            IsLoadingWebDavBackups = false;
        }
    }

    public async Task CreateWebDavBackupAsync()
    {
        if (!CanRunWebDavOperations)
        {
            return;
        }

        LastOperation = "WebDavBackup";
        IsCreatingWebDavBackup = true;
        var shouldRefresh = false;
        try
        {
            if (await ApplyWebDavAsync(
                    settings => _webDavService!.CreateBackupAsync(settings, CancellationToken.None),
                    _localization.GetString("Settings.Data.WebDav.Toast.BackupCreated"),
                    PrimaryWebDavBusyDuration))
            {
                _settings.LastWebDavBackupTime = _now();
                _settingsStore?.Save(_settings);
                shouldRefresh = IsWebDavBackupDialogVisible;
            }
        }
        finally
        {
            IsCreatingWebDavBackup = false;
        }

        if (shouldRefresh)
        {
            await RefreshWebDavBackupsAsync();
        }
    }

    public async Task RestoreWebDavBackupAsync(string? fileName)
    {
        var item = FindWebDavBackupItem(fileName);
        if (item is null || !CanRunWebDavOperations || IsWebDavBackupDialogBusy)
        {
            return;
        }

        LastOperation = "WebDavRestore";
        item.SetRestoring(true);
        RefreshWebDavBackupDialogState();
        try
        {
            var succeeded = await ApplyWebDavAsync(
                settings => _webDavService!.RestoreBackupAsync(settings, item.FileName, DataRestoreMode.Overwrite, CancellationToken.None),
                _localization.GetString("Settings.Data.Toast.RestoreCompleted"),
                PrimaryWebDavBusyDuration);
            if (succeeded)
            {
                RestoreCompleted?.Invoke(this, DataRestoreMode.Overwrite);
            }
        }
        finally
        {
            item.SetRestoring(false);
            RefreshWebDavBackupDialogState();
        }
    }

    public async Task DeleteWebDavBackupAsync(string? fileName)
    {
        var item = FindWebDavBackupItem(fileName);
        if (item is null || !CanRunWebDavOperations || IsWebDavBackupDialogBusy)
        {
            return;
        }

        LastOperation = "WebDavDelete";
        var shouldRemove = false;
        item.SetDeleting(true);
        RefreshWebDavBackupDialogState();
        try
        {
            if (await ApplyWebDavAsync(
                    settings => _webDavService!.DeleteBackupAsync(settings, item.FileName, CancellationToken.None),
                    _localization.GetString("Settings.Data.WebDav.Toast.BackupDeleted"),
                    DeleteWebDavBusyDuration))
            {
                shouldRemove = true;
            }
        }
        finally
        {
            item.SetDeleting(false);
            if (shouldRemove)
            {
                WebDavBackupItems.Remove(item);
                _cachedWebDavBackupEntries = _cachedWebDavBackupEntries
                    .Where(entry => !string.Equals(entry.FileName, item.FileName, StringComparison.Ordinal))
                    .ToList();
            }

            RefreshWebDavBackupDialogState();
        }
    }

    public async Task ConfirmRestoreAsync()
    {
        if (!IsRestoreDialogVisible)
        {
            return;
        }

        var restoreMode = SelectedRestoreMode;
        var backupPath = _pendingBackupPath;
        var isWebDavRestore = _isPendingWebDavRestore;
        BeginCloseRestoreDialog();
        LastOperation = isWebDavRestore ? "WebDavRestore" : "Restore";

        var succeeded = isWebDavRestore
            ? await ApplyWebDavAsync(
                settings => _webDavService!.RestoreLatestBackupAsync(settings, restoreMode, CancellationToken.None),
                _localization.GetString("Settings.Data.Toast.RestoreCompleted"))
            : Apply(() => string.IsNullOrWhiteSpace(backupPath)
                    ? _service?.RestoreBackup(restoreMode)
                    : _service?.RestoreBackup(backupPath, restoreMode),
                _localization.GetString("Settings.Data.Toast.RestoreCompleted"));
        if (succeeded)
        {
            RestoreCompleted?.Invoke(this, restoreMode);
        }
    }

    private void CancelRestore()
    {
        BeginCloseRestoreDialog();
    }

    private void BeginCloseRestoreDialog()
    {
        if (!IsRestoreDialogVisible)
        {
            return;
        }

        IsRestoreDialogVisible = false;
        _restoreCloseReset.Run(() => !IsRestoreDialogVisible, ResetRestoreDialog);
    }

    private void ResetRestoreDialog()
    {
        _pendingBackupPath = string.Empty;
        _isPendingWebDavRestore = false;
        SelectedRestoreMode = DataRestoreMode.Overwrite;
        OnPropertyChanged(nameof(RestoreTargetText));
    }

    // 外部错误保留日志，toast 保持稳定可读。
    private bool Apply(Func<DataManagementOperationResult?> operation, string? successMessage = null)
    {
        try
        {
            var result = operation();
            var isSuccess = result?.IsSuccess == true;
            if (result is { IsSuccess: false })
            {
                AppLogger.Warning($"Data management operation returned failure: {result.Message}");
            }

            var message = result is null
                ? null
                : isSuccess
                    ? SuccessMessage(successMessage, result.Message)
                    : _localization.GetString("Settings.Data.Toast.OperationFailed");
            if (!string.IsNullOrWhiteSpace(message))
            {
                ToastRequested?.Invoke(this, (message, isSuccess ? ToastType.Success : ToastType.Error));
            }

            return isSuccess;
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Data management operation failed: {exception.Message}");
            ToastRequested?.Invoke(this, (_localization.GetString("Settings.Data.Toast.OperationFailed"), ToastType.Error));
            return false;
        }
    }

    private async Task<bool> ApplyWebDavAsync(
        Func<WebDavBackupSettings, Task<DataManagementOperationResult>> operation,
        string? successMessage = null,
        TimeSpan? minimumBusyDuration = null)
    {
        if (_webDavService is null)
        {
            WebDavStatusText = _localization.GetString("Settings.Data.WebDav.Toast.NotAvailable");
            ToastRequested?.Invoke(this, (WebDavStatusText, ToastType.Error));
            return false;
        }

        if (IsWebDavBusy)
        {
            return false;
        }

        var minimumBusyTask = minimumBusyDuration is { } duration
            ? Task.Delay(duration)
            : Task.CompletedTask;
        IsWebDavBusy = true;
        try
        {
            var result = await operation(CurrentWebDavSettings());
            if (!result.IsSuccess)
            {
                AppLogger.Warning($"WebDAV data management operation returned failure: {result.Message}");
            }

            var message = result.IsSuccess
                ? SuccessMessage(successMessage, result.Message)
                : _localization.GetString("Settings.Data.WebDav.Toast.OperationFailed");
            WebDavStatusText = message;
            if (!string.IsNullOrWhiteSpace(message))
            {
                ToastRequested?.Invoke(this, (message, result.IsSuccess ? ToastType.Success : ToastType.Error));
            }

            return result.IsSuccess;
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"WebDAV data management operation failed: {exception.Message}");
            WebDavStatusText = _localization.GetString("Settings.Data.WebDav.Toast.OperationFailed");
            ToastRequested?.Invoke(this, (WebDavStatusText, ToastType.Error));
            return false;
        }
        finally
        {
            await minimumBusyTask;
            IsWebDavBusy = false;
        }
    }

    private static string SuccessMessage(string? preferredMessage, string fallbackMessage)
    {
        return string.IsNullOrWhiteSpace(preferredMessage) ? fallbackMessage : preferredMessage;
    }

    private WebDavBackupItemViewModel ToBackupItem(RemoteBackupEntry entry)
    {
        var displayName = Path.GetFileName(entry.FileName);
        var detailParts = new List<string>();
        if (entry.LastModified is { } lastModified)
        {
            detailParts.Add(lastModified.ToLocalTime().ToString("yyyy/MM/dd HH:mm"));
        }

        if (entry.Size is { } size)
        {
            detailParts.Add(FormatFileSize(size));
        }

        return new WebDavBackupItemViewModel(
            entry.FileName,
            displayName,
            string.Join(" · ", detailParts),
            SanitizeAutomationToken(entry.FileName));
    }

    private void UpdateWebDavBackupItems(IReadOnlyList<RemoteBackupEntry> entries)
    {
        if (_cachedWebDavBackupEntries.SequenceEqual(entries))
        {
            return;
        }

        _cachedWebDavBackupEntries = entries.ToList();
        WebDavBackupItems.Clear();
        foreach (var entry in entries)
        {
            WebDavBackupItems.Add(ToBackupItem(entry));
        }

        RefreshWebDavBackupItemLocks();
    }

    private void EnsureWebDavBackupCacheScope()
    {
        var cacheKey = WebDavBackupCacheKey(CurrentWebDavSettings());
        if (string.Equals(_webDavBackupCacheKey, cacheKey, StringComparison.Ordinal))
        {
            return;
        }

        _webDavBackupCacheKey = cacheKey;
        _cachedWebDavBackupEntries = [];
        WebDavBackupItems.Clear();
    }

    private static string WebDavBackupCacheKey(WebDavBackupSettings settings)
    {
        return string.Join('\u001f', settings.Url.Trim(), settings.RemoteDirectory.Trim(), settings.UserName.Trim());
    }

    private WebDavBackupItemViewModel? FindWebDavBackupItem(string? fileName)
    {
        return string.IsNullOrWhiteSpace(fileName)
            ? null
            : WebDavBackupItems.FirstOrDefault(item => string.Equals(item.FileName, fileName, StringComparison.Ordinal));
    }

    private void RefreshWebDavBackupItemLocks()
    {
        foreach (var item in WebDavBackupItems)
        {
            item.SetInteractionLocked(IsWebDavBackupDialogBusy);
        }
    }

    private void RefreshWebDavBackupDialogState()
    {
        OnPropertyChanged(nameof(CanRunWebDavOperations));
        OnPropertyChanged(nameof(CanManageWebDavBackups));
        OnPropertyChanged(nameof(IsWebDavBackupDialogBusy));
        RefreshWebDavBackupItemLocks();
    }

    private static string FormatFileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var size = Math.Max(0, bytes);
        var unitIndex = 0;
        var value = (double)size;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{size} {units[unitIndex]}"
            : $"{value:0.#} {units[unitIndex]}";
    }

    private static string SanitizeAutomationToken(string value)
    {
        var chars = value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        var readable = new string(chars).Trim('-');
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12].ToLowerInvariant();
        return string.IsNullOrWhiteSpace(readable) ? hash : $"{readable}-{hash}";
    }

    private WebDavBackupSettings CurrentWebDavSettings()
    {
        return new WebDavBackupSettings(
            WebDavUrl,
            WebDavRemoteDirectory,
            WebDavUserName,
            WebDavPassword,
            Math.Max(1, _settings.WebDavBackupRetentionCount));
    }

    private void SetSetting<T>(T currentValue, T nextValue, Action<T> assign, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(currentValue, nextValue))
        {
            return;
        }

        assign(nextValue);
        _settingsStore?.Save(_settings);
        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(CanManageWebDavBackups));
    }

    private void SetWebDavOptionValue(
        string value,
        IReadOnlyList<SelectionOption<int>> options,
        int currentValue,
        Action<int> assign,
        string selectedPropertyName,
        string textPropertyName)
    {
        if (!int.TryParse(value, out var parsed) || options.All(option => option.Value != parsed))
        {
            ToastRequested?.Invoke(this, (_localization.GetString("Settings.Toast.InvalidNumberRestored"), ToastType.Warning));
            OnPropertyChanged(textPropertyName);
            return;
        }

        SetSetting(currentValue, parsed, assign, textPropertyName);
        OnPropertyChanged(selectedPropertyName);
    }

    private string FormatHourOption(int hours)
    {
        return _localization.EffectiveLanguage switch
        {
            AppLanguage.ZhHans => $"{hours} 小时",
            AppLanguage.ZhHant => $"{hours} 小時",
            _ => hours == 1 ? "1 hour" : $"{hours} hours"
        };
    }

    private string FormatCountOption(int count)
    {
        return _localization.EffectiveLanguage is AppLanguage.ZhHans or AppLanguage.ZhHant
            ? $"{count} 份"
            : count == 1 ? "1 copy" : $"{count} copies";
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(BackupText));
        OnPropertyChanged(nameof(RestoreText));
        OnPropertyChanged(nameof(DescriptionText));
        OnPropertyChanged(nameof(BackupDescriptionText));
        OnPropertyChanged(nameof(RestoreDescriptionText));
        OnPropertyChanged(nameof(WebDavGroupText));
        OnPropertyChanged(nameof(WebDavEnableText));
        OnPropertyChanged(nameof(WebDavUrlText));
        OnPropertyChanged(nameof(WebDavUserNameText));
        OnPropertyChanged(nameof(WebDavPasswordText));
        OnPropertyChanged(nameof(WebDavRemoteDirectoryText));
        OnPropertyChanged(nameof(WebDavIntervalText));
        OnPropertyChanged(nameof(WebDavRetentionText));
        OnPropertyChanged(nameof(WebDavBackupIntervalOptions));
        OnPropertyChanged(nameof(SelectedWebDavBackupIntervalOption));
        OnPropertyChanged(nameof(WebDavBackupRetentionOptions));
        OnPropertyChanged(nameof(SelectedWebDavBackupRetentionOption));
        OnPropertyChanged(nameof(WebDavTestText));
        OnPropertyChanged(nameof(WebDavBackupText));
        OnPropertyChanged(nameof(WebDavRestoreText));
        OnPropertyChanged(nameof(WebDavDeleteText));
        OnPropertyChanged(nameof(WebDavManageText));
        OnPropertyChanged(nameof(WebDavBackupDialogTitleText));
        OnPropertyChanged(nameof(RestoreDialogTitleText));
        OnPropertyChanged(nameof(RestoreModeOverwriteText));
        OnPropertyChanged(nameof(RestoreModeOverwriteDescriptionText));
        OnPropertyChanged(nameof(RestoreModeMergeText));
        OnPropertyChanged(nameof(RestoreModeMergeDescriptionText));
        OnPropertyChanged(nameof(RestoreConfirmText));
        OnPropertyChanged(nameof(RestoreCancelText));
        OnPropertyChanged(nameof(RestoreTargetText));
        OnPropertyChanged(nameof(Items));
    }
}
