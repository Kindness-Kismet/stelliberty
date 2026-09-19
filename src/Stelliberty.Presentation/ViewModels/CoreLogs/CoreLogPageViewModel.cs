using System.Windows.Input;
using Stelliberty.Application.CoreLogs;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Domain.CoreLogs;
using Stelliberty.Application.Localization;
using Stelliberty.Presentation.Commands;

namespace Stelliberty.Presentation.ViewModels;

public sealed class CoreLogPageViewModel : ViewModelBase, IDisposable
{
    private readonly CoreLogFilter _filter = new();
    private readonly CoreLogReducer _reducer = new();
    private readonly ILocalizationService? _localization;
    private CoreLogState _state = CoreLogState.Initial;
    private bool _isCoreRunning = true;

    public event EventHandler? LogsCleared;

    public CoreLogPageViewModel(ILocalizationService? localization = null)
    {
        _localization = localization;
        if (_localization is not null)
        {
            _localization.LanguageChanged += OnLanguageChanged;
        }
        ShowAllLevelsCommand = new RelayCommand(() => SetFilterLevel(null));
        ShowDebugLevelCommand = new RelayCommand(() => SetFilterLevel(CoreLogLevel.Debug));
        ShowInfoLevelCommand = new RelayCommand(() => SetFilterLevel(CoreLogLevel.Info));
        ShowWarningLevelCommand = new RelayCommand(() => SetFilterLevel(CoreLogLevel.Warning));
        ShowErrorLevelCommand = new RelayCommand(() => SetFilterLevel(CoreLogLevel.Error));
        TogglePauseCommand = new RelayCommand(TogglePause);
        ClearLogsCommand = new RelayCommand(ClearLogs);
    }

    public PageLoadingState Loading { get; } = new();
    public IReadOnlyList<CoreLogMessage> Logs => _state.Logs;

    public IReadOnlyList<CoreLogMessage> FilteredLogs => _filter.Apply(_state.Logs, _state.FilterLevel, _state.SearchKeyword);

    public IReadOnlyList<CoreLogRowViewModel> FilteredLogRows => FilteredLogs
        .Reverse()
        .Select((log, index) => new CoreLogRowViewModel(index + 1, log, _localization))
        .ToList();

    public int TotalLogCount => Logs.Count;

    public int FilteredLogCount => FilteredLogs.Count;

    public int WarningLogCount => Logs.Count(log => log.Level == CoreLogLevel.Warning);

    public int ErrorLogCount => Logs.Count(log => log.Level == CoreLogLevel.Error);

    public string MonitorStateText => !IsCoreRunning
        ? Localize("CoreLogs.State.Stopped")
        : IsMonitoringPaused
            ? Localize("CoreLogs.State.Paused")
            : Localize("CoreLogs.State.Listening");

    public bool IsMonitoringPaused => _state.IsMonitoringPaused;

    public string MonitoringToggleTooltip => Localize(IsMonitoringPaused
        ? "CoreLogs.Action.Resume"
        : "CoreLogs.Action.Pause");

    public CoreLogLevel? FilterLevel => _state.FilterLevel;

    public bool IsAllLevelsSelected => _state.FilterLevel is null;
    public bool IsDebugLevelSelected => _state.FilterLevel == CoreLogLevel.Debug;
    public bool IsInfoLevelSelected => _state.FilterLevel == CoreLogLevel.Info;
    public bool IsWarningLevelSelected => _state.FilterLevel == CoreLogLevel.Warning;
    public bool IsErrorLevelSelected => _state.FilterLevel == CoreLogLevel.Error;

    public bool IsCoreRunning => _isCoreRunning;

    public bool IsEmptyVisible => !IsCoreRunning || FilteredLogs.Count == 0;

    public string EmptyText => !IsCoreRunning
        ? Localize("CoreLogs.Empty.CoreStopped")
        : Logs.Count == 0
            ? Localize("CoreLogs.Empty.NoLogs")
            : Localize("CoreLogs.Empty.NoMatches");

    public string SearchKeyword
    {
        get => _state.SearchKeyword;
        set
        {
            if (value == _state.SearchKeyword)
            {
                return;
            }

            var wasActive = !string.IsNullOrWhiteSpace(_state.SearchKeyword);
            _state = _state with { SearchKeyword = value };
            var isActive = !string.IsNullOrWhiteSpace(value);
            if (wasActive != isActive)
            {
                AppLogger.Info($"Core log search changed: active={isActive.ToString().ToLowerInvariant()} count={_state.Logs.Count}");
            }

            RaiseLogStateChanged();
        }
    }

    public ICommand ShowAllLevelsCommand { get; }
    public ICommand ShowDebugLevelCommand { get; }
    public ICommand ShowInfoLevelCommand { get; }
    public ICommand ShowWarningLevelCommand { get; }
    public ICommand ShowErrorLevelCommand { get; }
    public ICommand TogglePauseCommand { get; }
    public ICommand ClearLogsCommand { get; }

    public void SetFilterLevel(CoreLogLevel? level)
    {
        if (_state.FilterLevel == level)
        {
            return;
        }

        _state = _state with { FilterLevel = level };
        AppLogger.Info($"Core log filter changed: level={level?.ToString() ?? "All"} count={_state.Logs.Count}");
        RaiseLogStateChanged();
    }

    public void TogglePause()
    {
        _state = _reducer.TogglePause(_state);
        AppLogger.Info($"Core log monitoring changed: paused={_state.IsMonitoringPaused.ToString().ToLowerInvariant()} count={_state.Logs.Count}");
        RaiseLogStateChanged();
    }

    public void AppendLogs(IReadOnlyList<CoreLogMessage> logs)
    {
        var previousCount = _state.Logs.Count;
        _state = _reducer.Append(_state, logs);
        if (previousCount == 0 && _state.Logs.Count > 0)
        {
            var filter = _state.FilterLevel?.ToString() ?? "All";
            var searchActive = !string.IsNullOrWhiteSpace(_state.SearchKeyword);
            AppLogger.Info(
                $"Core log page received first batch: received={logs.Count} count={_state.Logs.Count} filter={filter} searchActive={searchActive.ToString().ToLowerInvariant()}");
        }

        RaiseLogStateChanged();
        Loading.CompleteInitialLoad();
    }

    public void ClearLogs()
    {
        var clearedCount = _state.Logs.Count;
        _state = _reducer.Clear(_state);
        AppLogger.Info($"Core logs cleared: count={clearedCount}");
        LogsCleared?.Invoke(this, EventArgs.Empty);
        RaiseLogStateChanged();
    }

    public void ApplyCoreRunning(bool isRunning)
    {
        if (_isCoreRunning == isRunning)
        {
            return;
        }

        _isCoreRunning = isRunning;
        if (!isRunning)
        {
            AppLogger.Info($"Core log page reset because core is not running: count={_state.Logs.Count}");
            _state = CoreLogState.Initial;
        }

        RaiseLogStateChanged();
    }

    private void RaiseLogStateChanged()
    {
        OnPropertyChanged(nameof(Logs));
        OnPropertyChanged(nameof(FilteredLogs));
        OnPropertyChanged(nameof(FilteredLogRows));
        OnPropertyChanged(nameof(TotalLogCount));
        OnPropertyChanged(nameof(FilteredLogCount));
        OnPropertyChanged(nameof(WarningLogCount));
        OnPropertyChanged(nameof(ErrorLogCount));
        OnPropertyChanged(nameof(MonitorStateText));
        OnPropertyChanged(nameof(IsMonitoringPaused));
        OnPropertyChanged(nameof(MonitoringToggleTooltip));
        OnPropertyChanged(nameof(FilterLevel));
        OnPropertyChanged(nameof(IsAllLevelsSelected));
        OnPropertyChanged(nameof(IsDebugLevelSelected));
        OnPropertyChanged(nameof(IsInfoLevelSelected));
        OnPropertyChanged(nameof(IsWarningLevelSelected));
        OnPropertyChanged(nameof(IsErrorLevelSelected));
        OnPropertyChanged(nameof(SearchKeyword));
        OnPropertyChanged(nameof(IsCoreRunning));
        OnPropertyChanged(nameof(IsEmptyVisible));
        OnPropertyChanged(nameof(EmptyText));
    }

    public void Dispose()
    {
        if (_localization is not null)
        {
            _localization.LanguageChanged -= OnLanguageChanged;
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs args)
    {
        RaiseLogStateChanged();
    }

    private string Localize(string key) => _localization?.GetString(key) ?? key;
}
