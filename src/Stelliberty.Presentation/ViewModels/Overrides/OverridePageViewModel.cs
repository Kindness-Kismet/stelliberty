using System.Windows.Input;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Localization;
using Stelliberty.Application.Overrides;
using Stelliberty.Domain.Overrides;
using Stelliberty.Application.Updates;
using Stelliberty.Presentation.Commands;
using static Stelliberty.Presentation.ViewModels.OverrideViewModelMapper;

namespace Stelliberty.Presentation.ViewModels;

public sealed class OverridePageViewModel : ViewModelBase, IDisposable
{
    private readonly IOverrideStore? _overrideStore;
    private readonly OverrideImporter? _overrideImporter;
    private readonly OverrideUpdater? _overrideUpdater;
    private readonly OverrideDeleter _overrideDeleter;
    private readonly OverrideReorderer? _reorderer;
    private readonly OverrideMetadataUpdater? _metadataUpdater;
    private readonly ILocalOverrideFileReader? _localFileReader;
    private readonly IOverrideFileOpener? _overrideFileOpener;
    private readonly ILocalizationService? _localization;
    private readonly List<OverrideItemViewModel> _overrides = [];
    private readonly UpdateOperationState _updateState = new();
    private readonly List<string> _deletedOverrideIds = [];
    private string? _currentOverrideId;
    private string? _deleteDialogOverrideId;

    public OverridePageViewModel(
        OverrideDeleter overrideDeleter,
        IOverrideStore? overrideStore = null,
        OverrideImporter? overrideImporter = null,
        OverrideUpdater? overrideUpdater = null,
        ILocalOverrideFileReader? localFileReader = null,
        IOverrideFileOpener? overrideFileOpener = null,
        ILocalizationService? localization = null)
    {
        _overrideStore = overrideStore;
        _reorderer = overrideStore is null ? null : new OverrideReorderer(overrideStore);
        _metadataUpdater = overrideStore is null ? null : new OverrideMetadataUpdater(overrideStore);
        _overrideImporter = overrideImporter;
        _overrideUpdater = overrideUpdater;
        _overrideDeleter = overrideDeleter;
        _localFileReader = localFileReader;
        _overrideFileOpener = overrideFileOpener;
        _localization = localization;
        FileEditor = new OverrideFileEditorViewModel();
        FileEditor.Confirmed += OnFileEditorConfirmed;
        FileEditor.DialogStateChanged += (_, _) => OnPropertyChanged(nameof(IsDialogOverlayVisible));
        EditDialog = new OverrideEditDialogViewModel(localization);
        EditDialog.Confirmed += OnEditDialogConfirmed;
        EditDialog.DialogStateChanged += (_, _) => OnPropertyChanged(nameof(IsDialogOverlayVisible));
        AddDialog = new OverrideAddDialogViewModel(localization);
        AddDialog.RemoteRequested += OnAddRemoteRequested;
        AddDialog.LocalRequested += OnAddLocalRequested;
        AddDialog.CreateBlankRequested += OnAddCreateBlankRequested;
        AddDialog.DialogStateChanged += OnAddDialogStateChanged;
        SelectOverrideCommand = new RelayCommand<string>(SelectOverride);
        UpdateOverrideCommand = new RelayCommand<string>(overrideId => _ = UpdateOverrideAsync(overrideId));
        UpdateAllOverridesCommand = new RelayCommand(() => _ = UpdateAllOverridesAsync());
        ShowEditDialogCommand = new RelayCommand<string>(ShowEditDialog);
        EditFileCommand = new RelayCommand<string>(EditFile);
        OpenExternalEditorCommand = new RelayCommand<string>(OpenExternalEditor);
        ShowDeleteDialogCommand = new RelayCommand<string>(ShowDeleteDialog);
        ConfirmDeleteCommand = new RelayCommand(ConfirmDelete);
        CancelDeleteDialogCommand = new RelayCommand(CancelDeleteDialog);
        DeleteOverrideCommand = new RelayCommand<string>(DeleteOverride);
        MoveOverrideCommand = new RelayCommand<OverrideMoveRequest>(MoveOverride);
        RowMenuActionCommand = new RelayCommand<OverrideRowMenuSelection>(selection =>
        {
            if (selection is not null)
            {
                RunRowMenuAction(selection);
            }
        });
        if (_localization is not null)
        {
            _localization.LanguageChanged += OnLanguageChanged;
        }
    }

    public event EventHandler<OverrideUpdateResult>? OverridesUpdated;

    public event EventHandler<(string Message, ToastType Type)>? ToastRequested;

    public event EventHandler<IReadOnlyList<string>>? OverridesEdited;

    public event EventHandler<OverrideDeleteResult>? OverrideDeleted;

    public IReadOnlyList<OverrideItemViewModel> Overrides => _overrides.ToList();

    public string? CurrentOverrideId => _currentOverrideId;

    public PageLoadingState Loading { get; } = new();
    public bool IsEmptyVisible => _overrides.Count == 0;

    public bool IsEmptyTextVisible => IsEmptyVisible;

    public bool IsListVisible => !IsEmptyVisible;

    public string EmptyText => Localize("Overrides.Empty.NoOverrides");

    public IReadOnlyList<string> UpdatedOverrideIds => _updateState.UpdatedItemIds;

    public IReadOnlyList<string> DeletedOverrideIds => _deletedOverrideIds;

    public bool HasUpdatedAllOverrides => _updateState.HasUpdatedAllItems;

    public bool IsBatchUpdatingOverrides => _updateState.IsBatchUpdating;

    public IReadOnlyList<string> UpdatingOverrideIds => _updateState.UpdatingItemIds;

    public bool CanUpdateAllOverrides => !IsBatchUpdatingOverrides && GetPendingOverrideUpdateIds().Count > 0;

    public bool IsUpdateAllIconVisible => !IsBatchUpdatingOverrides;

    public IReadOnlyList<string> SkippedOverrideUpdateIds => _updateState.SkippedItemIds;

    public OverrideEditDialogViewModel EditDialog { get; }

    public OverrideFileEditorViewModel FileEditor { get; }

    public string? DeleteDialogOverrideId => _deleteDialogOverrideId;

    public bool IsDeleteDialogVisible => _deleteDialogOverrideId is not null;

    public bool IsDialogOverlayVisible => AddDialog.IsDialogVisible || EditDialog.IsDialogVisible || FileEditor.IsDialogVisible || IsDeleteDialogVisible;

    public OverrideAddDialogViewModel AddDialog { get; }

    public ICommand SelectOverrideCommand { get; }
    public ICommand UpdateOverrideCommand { get; }
    public ICommand UpdateAllOverridesCommand { get; }
    public ICommand ShowEditDialogCommand { get; }
    public ICommand EditFileCommand { get; }
    public ICommand OpenExternalEditorCommand { get; }
    public ICommand ShowDeleteDialogCommand { get; }
    public ICommand ConfirmDeleteCommand { get; }
    public ICommand CancelDeleteDialogCommand { get; }
    public ICommand DeleteOverrideCommand { get; }
    public ICommand MoveOverrideCommand { get; }
    public ICommand RowMenuActionCommand { get; }

    public void AddOverride(OverrideItemViewModel item)
    {
        _overrides.Add(item);
        RaiseOverrideStateChanged();
    }

    public void LoadOverrides(IReadOnlyList<OverrideProfile> overrideProfiles)
    {
        var currentOverrideId = _currentOverrideId;
        var deleteDialogOverrideId = _deleteDialogOverrideId;
        _overrides.Clear();
        _overrides.AddRange(overrideProfiles.Select(overrideProfile => ToOverrideItem(overrideProfile, _localization)));
        _currentOverrideId = _overrides.Any(item => item.Id == currentOverrideId) ? currentOverrideId : null;
        _deleteDialogOverrideId = _overrides.Any(item => item.Id == deleteDialogOverrideId) ? deleteDialogOverrideId : null;
        RaiseOverrideStateChanged();
        RaiseMenuStateChanged();
        Loading.CompleteInitialLoad();
    }

    // 启动列表在后台加载并回到 UI 线程提交；失败只记日志。
    public async Task InitializeAsync()
    {
        using var loading = Loading.BeginLoading();
        if (_overrideStore is null)
        {
            return;
        }

        try
        {
            var overrides = await Task.Run(() => _overrideStore.LoadOverrides());
            LoadOverrides(overrides);
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Startup list load failed: {exception.Message}");
        }
    }

    public OverrideItemViewModel ApplyImportedOverride(OverrideProfile overrideProfile)
    {
        var item = ToOverrideItem(overrideProfile, _localization);
        AddOverride(item);
        AddDialog.Close();
        return item;
    }

    public void ApplyOverrideUpdateResult(OverrideUpdateResult result)
    {
        var resultIds = result.UpdatedOverrideIds.Concat(result.SkippedOverrideIds).ToHashSet(StringComparer.Ordinal);
        var completesBatch = resultIds.Any(_updateState.IsBatchUpdatingItem);
        if (!completesBatch && !resultIds.Any(_updateState.IsUpdating))
        {
            _updateState.StartBatchUpdate(resultIds.Select(id => new UpdateOperationItem(id, CanUpdate: result.UpdatedOverrideIds.Contains(id))).ToList());
            completesBatch = true;
        }

        foreach (var overrideId in result.SkippedOverrideIds)
        {
            _updateState.MarkItemSkipped(overrideId);
            _updateState.CompleteItemUpdate(overrideId);
        }

        foreach (var overrideId in result.UpdatedOverrideIds)
        {
            _updateState.CompleteItemUpdate(overrideId, isUpdated: true);
        }

        if (completesBatch)
        {
            _updateState.CompleteBatchUpdate();
        }

        RaiseOverrideStateChanged();
        OverridesUpdated?.Invoke(this, result);
    }

    private void ShowToast(string message, ToastType type = ToastType.Error)
    {
        ToastRequested?.Invoke(this, (message, type));
    }

    private void ShowSuccessToast(string localizationKey, string value)
    {
        ShowToast(string.Format(Localize(localizationKey), value), ToastType.Success);
    }

    private void ShowErrorToast(string localizationKey)
    {
        ShowToast(Localize(localizationKey));
    }

    private void OnAddDialogStateChanged(object? sender, EventArgs args)
    {
        OnPropertyChanged(nameof(IsEmptyTextVisible));
        OnPropertyChanged(nameof(IsListVisible));
        OnPropertyChanged(nameof(IsDialogOverlayVisible));
    }

    private async void OnAddRemoteRequested(object? sender, OverrideAddRemoteRequestedEventArgs args)
    {
        await AddRemoteOverrideAsync(args);
    }

    public async Task<OverrideItemViewModel?> AddRemoteOverrideAsync(OverrideAddRemoteRequestedEventArgs args)
    {
        var importer = _overrideImporter
            ?? throw new InvalidOperationException("Override importer is not initialized");
        var minDisplayTask = Task.Delay(600);
        try
        {
            var imported = await importer.ImportRemoteAsync(args.Name, args.SourceLocation, args.Format, args.UpdateProxyMode);
            await minDisplayTask;
            var importedItem = ApplyImportedOverride(imported);
            ShowSuccessToast("Overrides.Toast.ImportRemoteSucceeded", importedItem.Name);
            return importedItem;
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Remote override import failed");
            await minDisplayTask;
            AddDialog.EndSubmit();
            ShowErrorToast("Overrides.Toast.ImportRemoteFailed");
            return null;
        }
    }

    private async void OnAddLocalRequested(object? sender, OverrideAddLocalRequestedEventArgs args)
    {
        await AddLocalOverrideAsync(args);
    }

    public async Task<OverrideItemViewModel?> AddLocalOverrideAsync(OverrideAddLocalRequestedEventArgs args)
    {
        var importer = _overrideImporter
            ?? throw new InvalidOperationException("Override importer is not initialized");
        var reader = _localFileReader
            ?? throw new InvalidOperationException("Local override file reader is not initialized");
        var minDisplayTask = Task.Delay(600);
        try
        {
            await minDisplayTask;
            var importedItem = ApplyImportedOverride(importer.ImportLocal(
                args.Name,
                args.SourceLocation,
                args.Format,
                reader.ReadAllText(args.SourceLocation)));
            ShowSuccessToast("Overrides.Toast.ImportLocalSucceeded", importedItem.Name);
            return importedItem;
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Local override import failed");
            await minDisplayTask;
            AddDialog.EndSubmit();
            ShowErrorToast("Overrides.Toast.ImportLocalFailed");
            return null;
        }
    }

    private async void OnAddCreateBlankRequested(object? sender, OverrideAddCreateBlankRequestedEventArgs args)
    {
        await CreateBlankOverrideAsync(args);
    }

    public async Task<OverrideItemViewModel?> CreateBlankOverrideAsync(OverrideAddCreateBlankRequestedEventArgs args)
    {
        var importer = _overrideImporter
            ?? throw new InvalidOperationException("Override importer is not initialized");
        var minDisplayTask = Task.Delay(600);
        try
        {
            await minDisplayTask;
            var importedItem = ApplyImportedOverride(importer.CreateBlankLocal(args.Name, args.Format));
            ShowSuccessToast("Overrides.Toast.CreateBlankSucceeded", importedItem.Name);
            return importedItem;
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Blank override creation failed");
            await minDisplayTask;
            AddDialog.EndSubmit();
            ShowErrorToast("Overrides.Toast.CreateBlankFailed");
            return null;
        }
    }

    public void SelectOverride(string? overrideId)
    {
        if (_overrides.All(item => item.Id != overrideId))
        {
            return;
        }

        _currentOverrideId = overrideId;
        RaiseOverrideStateChanged();
    }

    public async Task UpdateOverrideAsync(string? overrideId, CancellationToken cancellationToken = default)
    {
        var item = _overrides.FirstOrDefault(item => item.Id == overrideId);
        if (item is null)
        {
            return;
        }
        var updater = _overrideUpdater
            ?? throw new InvalidOperationException("Override updater is not initialized");
        var store = _overrideStore
            ?? throw new InvalidOperationException("Override store is not initialized");

        if (_updateState.TryStartItemUpdate(new UpdateOperationItem(item.Id, CanUpdate: !item.IsLocalFile)) == UpdateStartResult.Skipped)
        {
            RaiseOverrideStateChanged();
            return;
        }

        RaiseOverrideStateChanged();
        var minDisplayTask = Task.Delay(600);
        try
        {
            var result = await updater.UpdateAsync(item.Id, cancellationToken);
            await minDisplayTask;
            ApplyOverrideUpdateResult(result);
            LoadOverrides(store.LoadOverrides());
            ShowOverrideUpdateToast(result.UpdatedOverrideIds.Contains(item.Id));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _updateState.CompleteItemUpdate(item.Id);
            RaiseOverrideStateChanged();
            throw;
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, $"Override update failed: {item.Name}");
            await minDisplayTask;
            _updateState.CompleteItemUpdate(item.Id);
            RaiseOverrideStateChanged();
            ShowOverrideUpdateToast(false);
        }
    }

    public async Task UpdateAllOverridesAsync(CancellationToken cancellationToken = default)
    {
        var overrideIds = GetPendingOverrideUpdateIds();
        if (overrideIds.Count == 0)
        {
            RaiseOverrideStateChanged();
            return;
        }
        var updater = _overrideUpdater
            ?? throw new InvalidOperationException("Override updater is not initialized");
        var store = _overrideStore
            ?? throw new InvalidOperationException("Override store is not initialized");
        if (_updateState.TryStartBatchUpdate(overrideIds.Select(item => new UpdateOperationItem(item, CanUpdate: true)).ToList()) == UpdateStartResult.Skipped)
        {
            RaiseOverrideStateChanged();
            return;
        }

        RaiseOverrideStateChanged();
        var minDisplayTask = Task.Delay(600);
        try
        {
            var result = await updater.UpdateManyAsync(overrideIds, cancellationToken);
            await minDisplayTask;
            ApplyOverrideUpdateResult(result);
            LoadOverrides(store.LoadOverrides());
            ShowOverrideBatchUpdateToast(result.UpdatedOverrideIds.Count, result.SkippedOverrideIds.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _updateState.CompleteBatchUpdate();
            RaiseOverrideStateChanged();
            throw;
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Updating all overrides failed");
            await minDisplayTask;
            _updateState.CompleteBatchUpdate();
            RaiseOverrideStateChanged();
            ShowOverrideBatchUpdateToast(0, overrideIds.Count);
        }
    }

    private void ShowOverrideUpdateToast(bool isSuccessful)
    {
        ShowToast(
            Localize(isSuccessful ? "Overrides.Toast.UpdateSucceeded" : "Overrides.Toast.UpdateFailed"),
            isSuccessful ? ToastType.Success : ToastType.Error);
    }

    private void ShowOverrideBatchUpdateToast(int succeededCount, int failedCount)
    {
        var type = failedCount == 0
            ? ToastType.Success
            : succeededCount == 0 ? ToastType.Error : ToastType.Warning;
        ShowToast(string.Format(Localize("Overrides.Toast.UpdateAllCompleted"), succeededCount, failedCount), type);
    }

    private void RunRowMenuAction(OverrideRowMenuSelection selection)
    {
        switch (selection.Action)
        {
            case OverrideRowMenuAction.Edit:
                ShowEditDialog(selection.OverrideId);
                break;
            case OverrideRowMenuAction.EditFile:
                EditFile(selection.OverrideId);
                break;
            case OverrideRowMenuAction.OpenExternalEditor:
                OpenExternalEditor(selection.OverrideId);
                break;
            case OverrideRowMenuAction.Delete:
                ShowDeleteDialog(selection.OverrideId);
                break;
        }
    }

    public void ShowEditDialog(string? overrideId)
    {
        var item = _overrides.FirstOrDefault(item => item.Id == overrideId);
        if (item is null)
        {
            return;
        }

        EditDialog.Open(item);
        RaiseMenuStateChanged();
    }

    private void OnEditDialogConfirmed(object? sender, OverrideEditCompletedEventArgs args)
    {
        var index = _overrides.FindIndex(item => item.Id == args.OverrideId);
        if (index < 0)
        {
            return;
        }

        var updated = _overrides[index];
        updated.UpdateConfiguration(
            args.Name,
            args.SourceLocation,
            args.Format,
            args.UpdateProxyMode);
        PersistEditedOverride(updated);
        OverridesEdited?.Invoke(this, [updated.Id]);
        RaiseOverrideStateChanged();
    }

    public void EditFile(string? overrideId)
    {
        var item = _overrides.FirstOrDefault(item => item.Id == overrideId);
        if (item is null)
        {
            return;
        }

        FileEditor.Open(item.Id, _overrideStore?.ReadContent(item.Id) ?? string.Empty, GetSyntaxLanguage(item.Format));
        RaiseMenuStateChanged();
    }

    public void OpenExternalEditor(string? overrideId)
    {
        var item = _overrides.FirstOrDefault(item => item.Id == overrideId);
        if (item is not null)
        {
            _overrideFileOpener?.OpenOverrideFile(item.Id);
        }
    }

    private void OnFileEditorConfirmed(object? sender, OverrideFileEditCompletedEventArgs args)
    {
        var item = _overrides.FirstOrDefault(item => item.Id == args.OverrideId);
        if (item is null)
        {
            return;
        }

        PersistEditedOverride(item, args.Content);
        OverridesEdited?.Invoke(this, [item.Id]);
    }

    public void ShowDeleteDialog(string? overrideId)
    {
        if (_overrides.All(item => item.Id != overrideId))
        {
            return;
        }

        _deleteDialogOverrideId = overrideId;
        RaiseMenuStateChanged();
    }

    private void ConfirmDelete()
    {
        DeleteOverride(_deleteDialogOverrideId);
    }

    private void CancelDeleteDialog()
    {
        _deleteDialogOverrideId = null;
        RaiseMenuStateChanged();
    }

    public void DeleteOverride(string? overrideId)
    {
        if (string.IsNullOrWhiteSpace(overrideId))
        {
            return;
        }

        var index = _overrides.FindIndex(item => item.Id == overrideId);
        if (index < 0)
        {
            if (_deleteDialogOverrideId == overrideId)
            {
                _deleteDialogOverrideId = null;
                RaiseMenuStateChanged();
            }

            return;
        }

        var result = _overrideDeleter.Delete(overrideId);
        _overrides.RemoveAt(index);
        _deletedOverrideIds.Add(overrideId);
        OverrideDeleted?.Invoke(this, result);
        ClearOverrideReferences(overrideId);
        RaiseOverrideStateChanged();
        RaiseMenuStateChanged();
    }

    private void ClearOverrideReferences(string overrideId)
    {
        if (_currentOverrideId == overrideId)
        {
            _currentOverrideId = null;
        }

        EditDialog.ClearForOverride(overrideId);

        FileEditor.ClearForOverride(overrideId);

        if (_deleteDialogOverrideId == overrideId)
        {
            _deleteDialogOverrideId = null;
        }
    }

    private void PersistEditedOverride(OverrideItemViewModel item, string? content = null)
    {
        _metadataUpdater?.Save(item.Id, new OverrideMetadataEdit(
            item.Name,
            item.SourceLocation,
            item.Format,
            item.UpdateProxyMode), content);
    }

    public void MoveOverrideUp(string? overrideId)
    {
        var index = _overrides.FindIndex(item => item.Id == overrideId);
        if (index <= 0)
        {
            return;
        }

        MoveOverrideTo(overrideId, index - 1);
    }

    public void MoveOverrideDown(string? overrideId)
    {
        var index = _overrides.FindIndex(item => item.Id == overrideId);
        if (index < 0 || index >= _overrides.Count - 1)
        {
            return;
        }

        MoveOverrideTo(overrideId, index + 1);
    }

    private void MoveOverride(OverrideMoveRequest? request)
    {
        if (request is null)
        {
            return;
        }

        MoveOverrideTo(request.OverrideId, request.TargetIndex);
    }

    private void MoveOverrideTo(string? overrideId, int targetIndex)
    {
        var index = _overrides.FindIndex(item => item.Id == overrideId);
        if (index < 0)
        {
            return;
        }

        var item = _overrides[index];
        _overrides.RemoveAt(index);
        _overrides.Insert(Math.Clamp(targetIndex, 0, _overrides.Count), item);
        PersistOverrideOrder();
        RaiseOverrideStateChanged();
    }

    private void PersistOverrideOrder()
    {
        _reorderer?.SaveOrder(_overrides.Select(item => item.Id).ToList());
    }

    private void SyncUpdatingOverrideRows()
    {
        foreach (var item in _overrides)
        {
            item.SetUpdating(_updateState.UpdatingItemIds.Contains(item.Id, StringComparer.Ordinal));
        }
    }

    private IReadOnlyList<string> GetPendingOverrideUpdateIds()
    {
        return _overrides
            .Where(item => !item.IsLocalFile && !_updateState.IsUpdating(item.Id))
            .Select(item => item.Id)
            .ToList();
    }

    private void RaiseOverrideStateChanged()
    {
        SyncUpdatingOverrideRows();
        OnPropertyChanged(nameof(Overrides));
        OnPropertyChanged(nameof(CurrentOverrideId));
        OnPropertyChanged(nameof(IsEmptyVisible));
        OnPropertyChanged(nameof(IsEmptyTextVisible));
        OnPropertyChanged(nameof(IsListVisible));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(UpdatedOverrideIds));
        OnPropertyChanged(nameof(UpdatingOverrideIds));
        OnPropertyChanged(nameof(CanUpdateAllOverrides));
        OnPropertyChanged(nameof(IsUpdateAllIconVisible));
        OnPropertyChanged(nameof(SkippedOverrideUpdateIds));
        OnPropertyChanged(nameof(DeletedOverrideIds));
        OnPropertyChanged(nameof(HasUpdatedAllOverrides));
        OnPropertyChanged(nameof(IsBatchUpdatingOverrides));
    }

    private void RaiseMenuStateChanged()
    {
        OnPropertyChanged(nameof(DeleteDialogOverrideId));
        OnPropertyChanged(nameof(IsDeleteDialogVisible));
        OnPropertyChanged(nameof(IsDialogOverlayVisible));
    }

    public void Dispose()
    {
        if (_localization is not null)
        {
            _localization.LanguageChanged -= OnLanguageChanged;
        }

        // 两个对话框都经基类订阅语言事件，必须一起释放
        AddDialog.Dispose();
        EditDialog.Dispose();
    }

    private void OnLanguageChanged(object? sender, EventArgs args)
    {
        foreach (var item in _overrides)
        {
            item.RefreshLanguage();
        }

        RaiseOverrideStateChanged();
        RaiseMenuStateChanged();
    }

    private string Localize(string key) => _localization?.GetString(key) ?? key;

    private static string GetSyntaxLanguage(OverrideFormat format)
    {
        return format == OverrideFormat.JavaScript ? "javascript" : "yaml";
    }
}
