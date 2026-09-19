using System.Collections.ObjectModel;
using System.Net;
using System.Windows.Input;
using Stelliberty.Application.Localization;
using Stelliberty.Application.Rules;
using Stelliberty.Domain.Rules;
using Stelliberty.Presentation.Commands;

namespace Stelliberty.Presentation.ViewModels;

public sealed class RulePageViewModel : ViewModelBase, IDisposable
{
    private readonly RuleOverrideService? _overrideService;
    private readonly RuleListLoader? _loader;
    private readonly ILocalizationService? _localization;
    private readonly RuleSearch _search = new();
    private static readonly IReadOnlyList<RuleTypeOptionViewModel> RuleTypeOptions = [
        new("DOMAIN", "DOMAIN"),
        new("DOMAIN-SUFFIX", "DOMAIN-SUFFIX"),
        new("DOMAIN-KEYWORD", "DOMAIN-KEYWORD"),
        new("IP-CIDR", "IP-CIDR"),
        new("IP-CIDR (no-resolve)", "IP-CIDR", "no-resolve"),
        new("IP-CIDR6", "IP-CIDR6"),
        new("IP-CIDR6 (no-resolve)", "IP-CIDR6", "no-resolve"),
        new("GEOIP", "GEOIP"),
        new("GEOIP (no-resolve)", "GEOIP", "no-resolve"),
        new("GEOSITE", "GEOSITE"),
        new("RULE-SET", "RULE-SET"),
        new("PROCESS-NAME", "PROCESS-NAME"),
        new("PROCESS-PATH", "PROCESS-PATH"),
        new("DST-PORT", "DST-PORT"),
        new("SRC-IP-CIDR", "SRC-IP-CIDR"),
        new("SRC-IP-CIDR (no-resolve)", "SRC-IP-CIDR", "no-resolve"),
        new("MATCH", "MATCH"),
    ];
    private static readonly IReadOnlyList<string> BuiltinOutboundActions = ["DIRECT", "REJECT", "REJECT-DROP"];
    private bool _isCoreRunning = true;
    private bool _hasRequestedRefresh;
    private bool _isEditorDialogVisible;
    private bool _isTemplateDialogVisible;
    private bool _isTemplateSelectMode;
    private RuleTypeOptionViewModel _selectedRuleType = RuleTypeOptions[1];
    private string _payload = string.Empty;
    private IReadOnlyList<OutboundTargetOptionViewModel> _outboundTargets = [];
    private OutboundTargetOptionViewModel? _selectedOutboundTarget;
    private string _templateName = string.Empty;
    private bool _hasAttemptedEditorSubmit;
    private bool _hasAttemptedTemplateSubmit;
    private string _payloadErrorKey = string.Empty;
    private string _templateNameErrorKey = string.Empty;
    private RuleTemplateOptionViewModel? _selectedTemplate;
    private RuleEditorRowViewModel? _editingRule;
    private RuleEditorRowViewModel? _deleteCandidate;
    private RuleEditorSnapshot _snapshot = new(string.Empty, [], [], false);
    private string _searchKeyword = string.Empty;
    private RuleTypeBucket _typeBucket = RuleTypeBucket.All;
    private IReadOnlyList<RuleItem> _rules = [];
    private IReadOnlyList<RuleItem> _filteredRules = [];
    private IReadOnlyList<RuleRowViewModel> _filteredRuleRows = [];

    public RulePageViewModel(RuleOverrideService? overrideService = null, ILocalizationService? localization = null)
    {
        _overrideService = overrideService;
        _localization = localization;
        _localization?.LanguageChanged += OnLanguageChanged;
        RefreshRulesCommand = new RelayCommand(RequestRefresh);
        ShowAllTypesCommand = new RelayCommand(() => SetTypeBucket(RuleTypeBucket.All));
        ShowDomainRulesCommand = new RelayCommand(() => SetTypeBucket(RuleTypeBucket.Domain));
        ShowIpRulesCommand = new RelayCommand(() => SetTypeBucket(RuleTypeBucket.Ip));
        ShowRuleSetRulesCommand = new RelayCommand(() => SetTypeBucket(RuleTypeBucket.RuleSet));
        ShowOtherRulesCommand = new RelayCommand(() => SetTypeBucket(RuleTypeBucket.Other));
        AddRuleCommand = new RelayCommand(() => OpenEditor(null));
        EditRuleCommand = new RelayCommand<RuleEditorRowViewModel>(row => OpenEditor(row));
        DeleteRuleCommand = new RelayCommand<RuleEditorRowViewModel>(ShowDeleteRuleDialog);
        MoveRuleCommand = new RelayCommand<RuleMoveRequest>(MoveRuleToIndex);
        SaveRuleCommand = new RelayCommand(SaveRule);
        SaveChangesCommand = new RelayCommand(SaveChanges);
        CancelEditorCommand = new RelayCommand(CloseEditor);
        OpenTemplateCommand = new RelayCommand(OpenTemplateSelector);
        OpenCreateTemplateCommand = new RelayCommand(OpenTemplateCreator);
        SaveTemplateCommand = new RelayCommand(SaveTemplate, () => CanSaveTemplate);
        ApplyTemplateCommand = new RelayCommand(ApplyTemplate, () => SelectedTemplate is not null);
        DeleteSingleTemplateCommand = new RelayCommand<RuleTemplateOptionViewModel>(DeleteSingleTemplate);
        CancelTemplateCommand = new RelayCommand(CancelTemplate);
        ConfirmDeleteRuleCommand = new RelayCommand(ConfirmDeleteRule);
        DeleteEditingRuleCommand = new RelayCommand(DeleteEditingRule);
        CancelDeleteRuleCommand = new RelayCommand(() => DeleteCandidate = null);
        ResetRuleOrderCommand = new RelayCommand(ResetRuleOrder, () => CanResetRuleOrder);
        RebuildOutboundTargets();
    }

    public RulePageViewModel(RuleListLoader loader, ILocalizationService? localization = null)
        : this(localization: localization)
    {
        _loader = loader;
    }

    public event EventHandler? RefreshRequested;
    public event EventHandler? RuntimeRefreshRequested;
    public event EventHandler<(string Message, ToastType Type)>? ToastRequested;
    public event EventHandler<DialogInputField>? InputFocusRequested;

    public ObservableCollection<RuleEditorRowViewModel> BuiltinRules { get; } = [];
    public ObservableCollection<RuleEditorRowViewModel> CustomRules { get; } = [];
    public ObservableCollection<RuleEditorRowViewModel> VisibleRules { get; } = [];
    public IReadOnlyList<RuleTypeOptionViewModel> RuleTypes => RuleTypeOptions;
    public IReadOnlyList<OutboundTargetOptionViewModel> OutboundTargets => _outboundTargets;
    public IReadOnlyList<RuleTemplateOptionViewModel> Templates => _snapshot.Templates.Select(template => new RuleTemplateOptionViewModel(template)).ToList();
    public IReadOnlyList<RuleItem> Rules => _rules;
    public IReadOnlyList<RuleItem> FilteredRules => _filteredRules;
    public IReadOnlyList<RuleRowViewModel> FilteredRuleRows => _filteredRuleRows;
    public RuleTypeBucket TypeBucket => _typeBucket;
    public bool IsAllTypesSelected => _typeBucket == RuleTypeBucket.All;
    public bool IsDomainRulesSelected => _typeBucket == RuleTypeBucket.Domain;
    public bool IsIpRulesSelected => _typeBucket == RuleTypeBucket.Ip;
    public bool IsRuleSetRulesSelected => _typeBucket == RuleTypeBucket.RuleSet;
    public bool IsOtherRulesSelected => _typeBucket == RuleTypeBucket.Other;
    public string SearchKeyword
    {
        get => _searchKeyword;
        set
        {
            if (SetProperty(ref _searchKeyword, value))
            {
                RebuildFilteredRows();
                RebuildVisibleRules();
            }
        }
    }
    public bool HasSubscription => _snapshot.HasSubscription;
    public bool IsCoreRunning => _isCoreRunning;
    public bool HasRequestedRefresh => _hasRequestedRefresh;
    public PageLoadingState Loading { get; } = new();
    public bool IsEmptyVisible => _overrideService is null
        ? !_isCoreRunning || _filteredRules.Count == 0
        : IsVisibleRulesEmpty;
    public bool IsCustomRulesEmpty => CustomRules.Count == 0;
    public bool HasCustomRules => CustomRules.Count > 0;
    public string CurrentSectionHint => Localize("Rules.Section.MixedHint");
    public bool IsTemplateSelectMode => _isTemplateSelectMode;
    public bool IsTemplateCreateMode => !_isTemplateSelectMode;
    public string TemplateDialogTitle => Localize(IsTemplateSelectMode ? "Rules.Dialog.Template.SelectTitle" : "Rules.Dialog.Template.CreateTitle");
    public bool IsVisibleRulesEmpty => VisibleRules.Count == 0;
    public bool HasSelectedTemplate => SelectedTemplate is not null;
    public bool CanSaveTemplate => HasCustomRules;
    public bool CanResetRuleOrder => HasSubscription && _snapshot.HasCustomOrder;
    public string EmptyText => Localize(EmptyTextKey);
    public string MonitorStateText => _isCoreRunning ? Localize("Rules.State.Monitoring") : Localize("Rules.State.CoreStopped");
    public string MonitorSignalTag => _isCoreRunning ? "ok" : "warning";

    // 只读态看运行时规则，编辑态看订阅快照；两者最后都是「无规则」或「无匹配」。
    private string EmptyTextKey
    {
        get
        {
            if (_overrideService is null)
            {
                if (!_isCoreRunning) return "Rules.Empty.CoreStopped";
                return _rules.Count == 0 ? "Rules.Empty.NoRules" : "Rules.Empty.NoMatches";
            }

            if (!HasSubscription) return "Rules.Empty.NoSubscription";
            return BuiltinRules.Count + CustomRules.Count == 0 ? "Rules.Empty.NoRules" : "Rules.Empty.NoMatches";
        }
    }

    public bool IsEditorDialogVisible { get => _isEditorDialogVisible; private set { if (SetProperty(ref _isEditorDialogVisible, value)) OnPropertyChanged(nameof(IsDialogOverlayVisible)); } }
    public bool IsTemplateDialogVisible { get => _isTemplateDialogVisible; private set { if (SetProperty(ref _isTemplateDialogVisible, value)) OnPropertyChanged(nameof(IsDialogOverlayVisible)); } }
    public bool IsDeleteDialogVisible => DeleteCandidate is not null;
    public bool IsDialogOverlayVisible => IsEditorDialogVisible || IsTemplateDialogVisible || IsDeleteDialogVisible;
    public bool IsEditingExisting => _editingRule is not null;
    public string EditorTitle => IsEditingExisting ? Localize("Rules.Dialog.Edit.Title") : Localize("Rules.Dialog.Add.Title");
    public RuleTypeOptionViewModel SelectedRuleType
    {
        get => _selectedRuleType;
        set
        {
            if (!SetProperty(ref _selectedRuleType, value))
            {
                return;
            }

            if (!IsPayloadEnabled)
            {
                _payload = string.Empty;
                _payloadErrorKey = string.Empty;
                OnPropertyChanged(nameof(Payload));
            }
            else if (_hasAttemptedEditorSubmit)
            {
                ValidatePayload();
            }

            OnPropertyChanged(nameof(IsPayloadEnabled));
            OnPropertyChanged(nameof(PayloadError));
            OnPropertyChanged(nameof(IsPayloadErrorVisible));
        }
    }
    public string Payload
    {
        get => _payload;
        set
        {
            if (SetProperty(ref _payload, value) && _hasAttemptedEditorSubmit)
            {
                ValidatePayload();
            }
        }
    }
    public OutboundTargetOptionViewModel? SelectedOutboundTarget
    {
        get => _selectedOutboundTarget;
        set => SetProperty(ref _selectedOutboundTarget, value);
    }
    public bool IsPayloadEnabled => !string.Equals(SelectedRuleType.Type, "MATCH", StringComparison.OrdinalIgnoreCase);
    public string PayloadError => LocalizeError(_payloadErrorKey);
    public bool IsPayloadErrorVisible => !string.IsNullOrEmpty(_payloadErrorKey);
    public string TemplateNameError => LocalizeError(_templateNameErrorKey);
    public bool IsTemplateNameErrorVisible => !string.IsNullOrEmpty(_templateNameErrorKey);
    public string TemplateName
    {
        get => _templateName;
        set
        {
            if (SetProperty(ref _templateName, value) && _hasAttemptedTemplateSubmit)
            {
                ValidateTemplateName();
            }
        }
    }
    public RuleEditorRowViewModel? DeleteCandidate
    {
        get => _deleteCandidate;
        private set
        {
            if (SetProperty(ref _deleteCandidate, value))
            {
                OnPropertyChanged(nameof(IsDeleteDialogVisible));
                OnPropertyChanged(nameof(IsDialogOverlayVisible));
            }
        }
    }
    public RuleTemplateOptionViewModel? SelectedTemplate { get => _selectedTemplate; set { if (SetProperty(ref _selectedTemplate, value)) { OnPropertyChanged(nameof(HasSelectedTemplate)); ((RelayCommand)ApplyTemplateCommand).RaiseCanExecuteChanged(); } } }

    public ICommand RefreshRulesCommand { get; }
    public ICommand ShowAllTypesCommand { get; }
    public ICommand ShowDomainRulesCommand { get; }
    public ICommand ShowIpRulesCommand { get; }
    public ICommand ShowRuleSetRulesCommand { get; }
    public ICommand ShowOtherRulesCommand { get; }
    public ICommand AddRuleCommand { get; }
    public ICommand EditRuleCommand { get; }
    public ICommand DeleteRuleCommand { get; }
    public ICommand MoveRuleCommand { get; }
    public ICommand SaveRuleCommand { get; }
    public ICommand SaveChangesCommand { get; }
    public ICommand CancelEditorCommand { get; }
    public ICommand OpenTemplateCommand { get; }
    public ICommand OpenCreateTemplateCommand { get; }
    public ICommand SaveTemplateCommand { get; }
    public ICommand ApplyTemplateCommand { get; }
    public ICommand DeleteSingleTemplateCommand { get; }
    public ICommand CancelTemplateCommand { get; }
    public ICommand ConfirmDeleteRuleCommand { get; }
    public ICommand DeleteEditingRuleCommand { get; }
    public ICommand CancelDeleteRuleCommand { get; }
    public ICommand ResetRuleOrderCommand { get; }

    private void OpenTemplateSelector()
    {
        ResetTemplateValidation();
        _isTemplateSelectMode = true;
        SelectedTemplate = null;
        OnPropertyChanged(nameof(IsTemplateSelectMode));
        OnPropertyChanged(nameof(IsTemplateCreateMode));
        OnPropertyChanged(nameof(TemplateDialogTitle));
        IsTemplateDialogVisible = true;
    }

    private void DeleteSingleTemplate(RuleTemplateOptionViewModel? template)
    {
        if (template is null || _overrideService is null) return;
        _overrideService.DeleteTemplate(template.Id);
        if (ReferenceEquals(SelectedTemplate, template) || SelectedTemplate?.Id == template.Id)
        {
            SelectedTemplate = null;
        }
        LoadEditorSnapshot();
    }

    private void OpenTemplateCreator()
    {
        ResetTemplateValidation();
        _isTemplateSelectMode = false;
        OnPropertyChanged(nameof(IsTemplateSelectMode));
        OnPropertyChanged(nameof(IsTemplateCreateMode));
        OnPropertyChanged(nameof(TemplateDialogTitle));
        IsTemplateDialogVisible = true;
    }

    private void CancelTemplate()
    {
        IsTemplateDialogVisible = false;
        ResetTemplateValidation();
    }

    public void LoadEditorSnapshot()
    {
        using var loading = Loading.HasLoaded ? null : Loading.BeginLoading();
        if (_overrideService is null)
        {
            return;
        }

        _snapshot = _overrideService.LoadCurrent();
        BuiltinRules.Clear();
        CustomRules.Clear();
        foreach (var item in _snapshot.Items.Where(item => item.IsBuiltIn))
        {
            var row = new RuleEditorRowViewModel(item);
            row.StateChanged += OnRuleStateChanged;
            BuiltinRules.Add(row);
        }
        foreach (var item in _snapshot.Items.Where(item => !item.IsBuiltIn))
        {
            var row = new RuleEditorRowViewModel(item);
            row.StateChanged += OnRuleStateChanged;
            CustomRules.Add(row);
        }

        _rules = _snapshot.Items.Select(item => new RuleItem(item.Type, item.Payload, item.Proxy, item.Options, item.Source, item.RuleCount)).ToList();
        RebuildFilteredRows();

        OnPropertyChanged(nameof(Templates));
        RebuildOutboundTargets();
        OnPropertyChanged(nameof(HasSubscription));
        OnPropertyChanged(nameof(IsEmptyVisible));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(IsCustomRulesEmpty));
        OnPropertyChanged(nameof(HasCustomRules));
        OnPropertyChanged(nameof(CanSaveTemplate));
        OnPropertyChanged(nameof(CanResetRuleOrder));
        ((RelayCommand)SaveTemplateCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ResetRuleOrderCommand).RaiseCanExecuteChanged();
        RebuildVisibleRules();
    }

    public void ApplyCoreRunning(bool isRunning)
    {
        if (SetProperty(ref _isCoreRunning, isRunning))
        {
            if (!isRunning && _overrideService is null)
            {
                _searchKeyword = string.Empty;
                _typeBucket = RuleTypeBucket.All;
                RebuildFilteredRows();
                OnPropertyChanged(nameof(SearchKeyword));
                OnPropertyChanged(nameof(TypeBucket));
            }
            OnPropertyChanged(nameof(IsEmptyVisible));
            OnPropertyChanged(nameof(EmptyText));
            OnPropertyChanged(nameof(MonitorStateText));
            OnPropertyChanged(nameof(MonitorSignalTag));
        }
    }

    private void RequestRefresh()
    {
        using var loading = Loading.HasLoaded ? null : Loading.BeginLoading();
        _hasRequestedRefresh = true;
        if (_overrideService is not null)
        {
            LoadEditorSnapshot();
        }
        else if (_loader is not null)
        {
            LoadRules(_loader.LoadRules());
        }
        OnPropertyChanged(nameof(HasRequestedRefresh));
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OpenEditor(RuleEditorRowViewModel? row)
    {
        _editingRule = row;
        SelectedRuleType = row is null ? RuleTypeOptions[1] : FindRuleType(row.Type, row.Options);
        Payload = row?.Item.Payload ?? string.Empty;
        SelectOutboundTarget(row?.Proxy ?? "DIRECT");
        ResetEditorValidation();
        IsEditorDialogVisible = true;
        OnPropertyChanged(nameof(IsEditingExisting));
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(IsDialogOverlayVisible));
    }

    private void CloseEditor()
    {
        IsEditorDialogVisible = false;
        _editingRule = null;
        OnPropertyChanged(nameof(IsEditingExisting));
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(IsDialogOverlayVisible));
    }

    private void SaveRule()
    {
        if (_overrideService is null || string.IsNullOrWhiteSpace(_snapshot.SubscriptionId))
        {
            return;
        }

        if (!ValidateEditorInputs())
        {
            FocusFirstInvalidEditorInput();
            return;
        }

        var id = _editingRule?.Id ?? $"custom-{Guid.NewGuid():N}";
        var rule = new EditableRule(id, SelectedRuleType.Type, Payload, SelectedOutboundTarget?.Value ?? string.Empty, SelectedRuleType.Options);
        var custom = CustomRules.Select(row => row.ToEditableRule()).ToList();
        if (_editingRule is not null)
        {
            var index = custom.FindIndex(item => item.Id == _editingRule.Id);
            custom[index] = rule;
        }
        else
        {
            custom.Add(rule);
        }

        try
        {
            SaveCurrentRules(custom);
            CloseEditor();
            LoadEditorSnapshot();
            RuntimeRefreshRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (RuleOverrideException exception)
        {
            ApplyEditorSaveError(exception.Error);
        }
    }

    private void SaveChanges()
    {
        if (_overrideService is null || string.IsNullOrWhiteSpace(_snapshot.SubscriptionId)) return;
        try
        {
            SaveCurrentRules(CustomRules.Select(row => row.ToEditableRule()).ToList());
            RuntimeRefreshRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (RuleOverrideException exception) { ShowRuleSaveToast(exception.Error); }
    }

    private void SaveCurrentRules(IReadOnlyList<EditableRule> customRules, IReadOnlyList<string>? ruleOrder = null)
    {
        if (_overrideService is null || string.IsNullOrWhiteSpace(_snapshot.SubscriptionId)) return;
        _overrideService.Save(
            _snapshot.SubscriptionId,
            customRules,
            BuiltinRules.Where(row => !row.IsEnabled).Select(row => row.Item.Key).ToHashSet(StringComparer.Ordinal),
            ruleOrder ?? VisibleRules.Select(row => row.OrderId).ToList());
    }

    private void DeleteRule(RuleEditorRowViewModel? row)
    {
        if (row is null || _overrideService is null || string.IsNullOrWhiteSpace(_snapshot.SubscriptionId)) return;
        var custom = CustomRules.Where(item => item.OrderId != row.OrderId).Select(item => item.ToEditableRule()).ToList();
        SaveCurrentRules(custom, VisibleRules.Where(item => item.OrderId != row.OrderId).Select(item => item.OrderId).ToList());
        LoadEditorSnapshot();
        RuntimeRefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ShowDeleteRuleDialog(RuleEditorRowViewModel? row)
    {
        if (row is not null)
        {
            DeleteCandidate = row;
        }
    }

    private void ConfirmDeleteRule()
    {
        var row = DeleteCandidate;
        DeleteCandidate = null;
        DeleteRule(row);
    }

    private void DeleteEditingRule()
    {
        var row = _editingRule;
        CloseEditor();
        ShowDeleteRuleDialog(row);
    }

    public void EditRule(RuleEditorRowViewModel row) => OpenEditor(row);

    // 传空 ruleOrder 即清除自定义排序，落库后按订阅原文顺序重建。
    private void ResetRuleOrder()
    {
        if (_overrideService is null || string.IsNullOrWhiteSpace(_snapshot.SubscriptionId))
        {
            return;
        }

        try
        {
            SaveCurrentRules(CustomRules.Select(row => row.ToEditableRule()).ToList(), []);
            LoadEditorSnapshot();
            RuntimeRefreshRequested?.Invoke(this, EventArgs.Empty);
            ToastRequested?.Invoke(this, (Localize("Rules.Toast.OrderReset"), ToastType.Success));
        }
        catch (RuleOverrideException exception)
        {
            ShowRuleSaveToast(exception.Error);
        }
    }

    private void MoveRuleToIndex(RuleMoveRequest? request)
    {
        if (request is null || !string.IsNullOrWhiteSpace(SearchKeyword) || _typeBucket != RuleTypeBucket.All) return;
        var source = VisibleRules.FirstOrDefault(item => item.OrderId == request.RuleId);
        if (source is null) return;
        var sourceIndex = VisibleRules.IndexOf(source);
        var targetIndex = Math.Clamp(request.TargetIndex, 0, VisibleRules.Count - 1);
        if (sourceIndex < 0 || targetIndex == sourceIndex) return;

        VisibleRules.RemoveAt(sourceIndex);
        VisibleRules.Insert(targetIndex, source);
        ReindexVisibleRules();
        try
        {
            SaveCurrentRules(CustomRules.Select(row => row.ToEditableRule()).ToList());
            RuntimeRefreshRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (RuleOverrideException exception) { ShowRuleSaveToast(exception.Error); }
    }

    private void SaveTemplate()
    {
        if (_overrideService is null || string.IsNullOrWhiteSpace(_snapshot.SubscriptionId)) return;
        _hasAttemptedTemplateSubmit = true;
        ValidateTemplateName();
        if (IsTemplateNameErrorVisible)
        {
            InputFocusRequested?.Invoke(this, DialogInputField.TemplateName);
            return;
        }

        var existing = _snapshot.Templates.FirstOrDefault(template => string.Equals(template.Name, TemplateName.Trim(), StringComparison.OrdinalIgnoreCase));
        var template = new RuleTemplate(existing?.Id ?? $"template-{Guid.NewGuid():N}", TemplateName.Trim(), CustomRules.Select(row => row.ToEditableRule()).ToList());
        _overrideService.UpsertTemplate(template);
        TemplateName = string.Empty;
        IsTemplateDialogVisible = false;
        LoadEditorSnapshot();
    }

    private void ApplyTemplate()
    {
        if (SelectedTemplate is null || _overrideService is null || string.IsNullOrWhiteSpace(_snapshot.SubscriptionId)) return;
        var custom = CustomRules.Select(row => row.ToEditableRule()).Concat(SelectedTemplate.Template.Rules).GroupBy(rule => rule.Key, StringComparer.Ordinal).Select(group => group.First()).ToList();
        try
        {
            SaveCurrentRules(custom);
            IsTemplateDialogVisible = false;
            SelectedTemplate = null;
            LoadEditorSnapshot();
            RuntimeRefreshRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (RuleOverrideException exception) { ShowRuleSaveToast(exception.Error); }
    }

    private void ResetEditorValidation()
    {
        _hasAttemptedEditorSubmit = false;
        _payloadErrorKey = string.Empty;
        RaiseEditorValidationChanged();
    }

    private void ResetTemplateValidation()
    {
        _hasAttemptedTemplateSubmit = false;
        _templateNameErrorKey = string.Empty;
        OnPropertyChanged(nameof(TemplateNameError));
        OnPropertyChanged(nameof(IsTemplateNameErrorVisible));
    }

    private bool ValidateEditorInputs()
    {
        _hasAttemptedEditorSubmit = true;
        ValidatePayload();
        return !IsPayloadErrorVisible;
    }

    private void FocusFirstInvalidEditorInput()
    {
        if (IsPayloadErrorVisible)
        {
            InputFocusRequested?.Invoke(this, DialogInputField.Payload);
        }
    }

    private void ValidatePayload()
    {
        _payloadErrorKey = string.Empty;
        if (IsPayloadEnabled && string.IsNullOrWhiteSpace(Payload))
        {
            _payloadErrorKey = "Rules.Error.PayloadRequired";
        }
        else if (IsPayloadEnabled && Payload.Contains(',', StringComparison.Ordinal))
        {
            _payloadErrorKey = "Rules.Error.PayloadDelimiter";
        }
        else if (RequiresCidrPayload(SelectedRuleType.Type) && !IsValidCidr(Payload, SelectedRuleType.Type))
        {
            _payloadErrorKey = "Rules.Error.Cidr";
        }
        else if (IsPayloadEnabled)
        {
            var matchKey = RuleKey.CreateMatch(SelectedRuleType.Type, Payload, SelectedRuleType.Options);
            if (CustomRules.Any(row => row.Id != _editingRule?.Id
                && string.Equals(row.Item.MatchKey, matchKey, StringComparison.Ordinal)))
            {
                _payloadErrorKey = "Rules.Error.DuplicateCustom";
            }
            else if (BuiltinRules.Any(row => row.IsEnabled
                && string.Equals(row.Item.MatchKey, matchKey, StringComparison.Ordinal)))
            {
                _payloadErrorKey = "Rules.Error.DuplicateBuiltin";
            }
        }

        OnPropertyChanged(nameof(PayloadError));
        OnPropertyChanged(nameof(IsPayloadErrorVisible));
    }

    private static bool RequiresCidrPayload(string type)
        => type.Equals("IP-CIDR", StringComparison.OrdinalIgnoreCase)
            || type.Equals("IP-CIDR6", StringComparison.OrdinalIgnoreCase)
            || type.Equals("SRC-IP-CIDR", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidCidr(string value, string type)
    {
        var parts = value.Trim().Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !IPAddress.TryParse(parts[0], out var address)
            || !int.TryParse(parts[1], out var prefixLength))
        {
            return false;
        }

        var requiresIpv6 = type.Equals("IP-CIDR6", StringComparison.OrdinalIgnoreCase);
        var maximumPrefixLength = requiresIpv6 ? 128 : 32;
        return (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6) == requiresIpv6
            && prefixLength >= 0
            && prefixLength <= maximumPrefixLength;
    }

    private void ValidateTemplateName()
    {
        _templateNameErrorKey = string.IsNullOrWhiteSpace(TemplateName)
            ? "Rules.Error.TemplateNameRequired"
            : string.Empty;
        OnPropertyChanged(nameof(TemplateNameError));
        OnPropertyChanged(nameof(IsTemplateNameErrorVisible));
    }

    private void ApplyEditorSaveError(RuleOverrideError error)
    {
        if (IsPayloadEnabled
            && error is RuleOverrideError.DuplicateCustomRule or RuleOverrideError.DuplicateBuiltinRule)
        {
            _payloadErrorKey = error == RuleOverrideError.DuplicateCustomRule
                ? "Rules.Error.DuplicateCustom"
                : "Rules.Error.DuplicateBuiltin";
            OnPropertyChanged(nameof(PayloadError));
            OnPropertyChanged(nameof(IsPayloadErrorVisible));
            InputFocusRequested?.Invoke(this, DialogInputField.Payload);
            return;
        }

        ShowRuleSaveToast(error);
    }

    private void ShowRuleSaveToast(RuleOverrideError error)
        => ToastRequested?.Invoke(this, (LocalizeRuleError(error), ToastType.Error));

    private void RaiseEditorValidationChanged()
    {
        OnPropertyChanged(nameof(PayloadError));
        OnPropertyChanged(nameof(IsPayloadErrorVisible));
    }

    private static RuleTypeOptionViewModel FindRuleType(string type, string options)
        => RuleTypeOptions.FirstOrDefault(option => string.Equals(option.Type, type, StringComparison.OrdinalIgnoreCase)
            && string.Equals(option.Options, options, StringComparison.OrdinalIgnoreCase)) ?? RuleTypeOptions[1];

    private void RebuildOutboundTargets()
    {
        var previousValue = _selectedOutboundTarget?.Value;
        var names = _snapshot.ProxyOptions.Count > 0 ? _snapshot.ProxyOptions : BuiltinOutboundActions;
        _outboundTargets = names.Select(name => new OutboundTargetOptionViewModel(name, name)).ToList();
        OnPropertyChanged(nameof(OutboundTargets));
        if (_selectedOutboundTarget is not null)
        {
            SelectOutboundTarget(previousValue ?? string.Empty);
        }
    }

    private void SelectOutboundTarget(string target)
    {
        SelectedOutboundTarget = _outboundTargets.FirstOrDefault(item => string.Equals(item.Value, target, StringComparison.Ordinal))
            ?? _outboundTargets.FirstOrDefault();
    }

    private string Localize(string key) => _localization?.GetString(key) ?? key;
    private string LocalizeError(string key) => string.IsNullOrEmpty(key) ? string.Empty : Localize(key);
    private string LocalizeRuleError(RuleOverrideError error) => Localize(error switch
    {
        RuleOverrideError.DuplicateCustomRule => "Rules.Error.DuplicateCustom",
        RuleOverrideError.DuplicateBuiltinRule => "Rules.Error.DuplicateBuiltin",
        RuleOverrideError.SubscriptionNotFound => "Rules.Error.SubscriptionNotFound",
        _ => "Rules.Error.InvalidRule",
    });
    private void OnRuleStateChanged(object? sender, EventArgs args) => SaveChanges();

    public void SetTypeBucket(RuleTypeBucket bucket)
    {
        if (_typeBucket == bucket) return;
        _typeBucket = bucket;
        OnPropertyChanged(nameof(TypeBucket));
        OnPropertyChanged(nameof(IsAllTypesSelected));
        OnPropertyChanged(nameof(IsDomainRulesSelected));
        OnPropertyChanged(nameof(IsIpRulesSelected));
        OnPropertyChanged(nameof(IsRuleSetRulesSelected));
        OnPropertyChanged(nameof(IsOtherRulesSelected));
        RebuildFilteredRows();
        RebuildVisibleRules();
    }

    public void LoadRules(IReadOnlyList<RuleItem> rules)
    {
        _rules = rules;
        RebuildFilteredRows();
        OnPropertyChanged(nameof(IsEmptyVisible));
        OnPropertyChanged(nameof(EmptyText));
        Loading.CompleteInitialLoad();
    }

    private void RebuildFilteredRows()
    {
        _filteredRules = _search.Filter(_rules, _searchKeyword)
            .Where(rule => RuleTypeClassifier.MatchesBucket(rule.Type, _typeBucket))
            .ToList();
        _filteredRuleRows = _filteredRules.Select((rule, index) => new RuleRowViewModel(index + 1, rule, _localization)).ToList();
        OnPropertyChanged(nameof(Rules));
        OnPropertyChanged(nameof(FilteredRules));
        OnPropertyChanged(nameof(FilteredRuleRows));
        OnPropertyChanged(nameof(IsEmptyVisible));
        OnPropertyChanged(nameof(EmptyText));
    }

    private void RebuildVisibleRules()
    {
        VisibleRules.Clear();
        var order = _snapshot.Items.Select(item => item.OrderId).ToList();
        var source = BuiltinRules.Concat(CustomRules)
            .OrderBy(row => OrderIndex(order, row.OrderId))
            .ToList();
        ReindexRows(source);

        var keyword = _searchKeyword.Trim();
        foreach (var row in source)
        {
            if (!RuleTypeClassifier.MatchesBucket(row.Type, _typeBucket))
            {
                continue;
            }

            if (keyword.Length > 0
                && !string.Join(' ', row.Type, row.Payload, row.Proxy, row.Options)
                    .Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            VisibleRules.Add(row);
        }

        OnPropertyChanged(nameof(CurrentSectionHint));
        OnPropertyChanged(nameof(IsVisibleRulesEmpty));
        OnPropertyChanged(nameof(IsEmptyVisible));
        OnPropertyChanged(nameof(EmptyText));
    }

    private static int OrderIndex(IReadOnlyList<string> order, string orderId)
    {
        for (var index = 0; index < order.Count; index++)
        {
            if (string.Equals(order[index], orderId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    private static void ReindexRows(IReadOnlyList<RuleEditorRowViewModel> rows)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            rows[index].SequenceNumber = index + 1;
        }
    }

    private void ReindexVisibleRules() => ReindexRows(VisibleRules.ToList());

    private void OnLanguageChanged(object? sender, EventArgs args)
    {
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(MonitorStateText));
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(TemplateDialogTitle));
        RaiseEditorValidationChanged();
        if (_hasAttemptedTemplateSubmit)
        {
            ValidateTemplateName();
        }
        OnPropertyChanged(nameof(TemplateNameError));
        RebuildOutboundTargets();
    }

    public void Dispose() => _localization?.LanguageChanged -= OnLanguageChanged;
}
