using System.Windows.Input;
using Stelliberty.Application.Connections;
using Stelliberty.Domain.Connections;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Localization;
using Stelliberty.Application.Proxies;
using Stelliberty.Domain.Proxies;
using Stelliberty.Presentation.Collections;
using Stelliberty.Presentation.Commands;

namespace Stelliberty.Presentation.ViewModels;

public sealed class ProxyPageViewModel : ViewModelBase, IDisposable
{
    private readonly ProxyNodeSorter _sorter = new();
    private readonly ProxyDelayService? _delayService;
    private readonly IProxyCoreClient? _coreClient;
    private readonly ProxySelectionService _selectionService;
    private readonly IProxyConfigProvider? _primaryConfigProvider;
    private readonly IProxyConfigProvider? _fallbackConfigProvider;
    private readonly ILocalizationService? _localization;
    private readonly Action<ProxyNodeSortMode>? _persistSortMode;
    private readonly Action<ProxyPageLayout>? _persistLayout;
    private ProxyConfig _config = new([], new Dictionary<string, ProxyNode>());
    private IReadOnlyList<ProxyGroup> _visibleGroups = [];
    private IReadOnlyDictionary<string, ProxyNode> _entryNodes = new Dictionary<string, ProxyNode>(StringComparer.Ordinal);
    private IReadOnlyList<ProxyGroupButtonViewModel> _visibleGroupRows = [];
    private IReadOnlyList<ProxyGroupCardViewModel> _visibleGroupCards = [];
    private readonly LazyRowList<string, ProxyNodeRowViewModel> _visibleNodeRows;
    private IReadOnlyList<string> _visibleNodeNames = [];
    private IReadOnlyDictionary<string, int> _visibleNodeIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
    private bool _isEmptyVisible = true;
    private string _emptyText = string.Empty;
    private string _emptySubtitle = string.Empty;
    private string _emptyIconType = "EarthLine";
    private ProxyGroup? _selectedGroup;
    private ProxyNodeSortMode _sortMode;
    private string _searchKeyword = string.Empty;
    private ProxyPageLayout _layoutMode;
    private string? _expandedGroupName;
    private Domain.Proxies.OutboundMode _outboundMode = Domain.Proxies.OutboundMode.Rule;
    private bool _isCoreRunning;
    private readonly HashSet<string> _delayTestedNodeNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _batchDelayTestedNodeNames = new(StringComparer.Ordinal);

    private string? _lastSelectedNodeName;
    private string? _locatedNodeName;
    private int _locateNodeRequestId;
    private ProxyChangeRequest? _lastChangeRequest;
    private readonly DelayTestCoordinator _delayTests = new();
    private bool _shouldCloseConnectionsAfterSelection;
    private bool _shouldChangeCoreOnSelection = true;
    private bool _shouldTestDelaysThroughService = true;
    private bool _hasScrolledToTop;
    private int _scrollToTopRequestId;
    private int _configVersion;
    private bool _hasLoadedConfig;
    private bool _isInitialLoadCompleted;
    private bool _isPresentationActive;
    private bool _isPresentationCacheReleased;
    private string? _loadedSubscriptionId;
    private int _externalSelectionSyncRunning;
    private readonly ResilientProxyConfigLoader _resilientLoader = new();

    public ProxyPageViewModel(
        ProxyDelayService? delayService = null,
        IProxyCoreClient? coreClient = null,
        IProxyConfigProvider? primaryConfigProvider = null,
        IProxyConfigProvider? fallbackConfigProvider = null,
        ILocalizationService? localization = null,
        ProxySelectionService? selectionService = null,
        ProxyPageLayout initialLayout = ProxyPageLayout.Horizontal,
        Action<ProxyPageLayout>? persistLayout = null,
        ProxyNodeSortMode initialSortMode = ProxyNodeSortMode.Default,
        Action<ProxyNodeSortMode>? persistSortMode = null,
        bool isPresentationActive = true)
    {
        _delayService = delayService;
        _coreClient = coreClient;
        _selectionService = selectionService ?? new ProxySelectionService(coreClient);
        _primaryConfigProvider = primaryConfigProvider;
        _fallbackConfigProvider = fallbackConfigProvider;
        _localization = localization;
        _sortMode = initialSortMode;
        _persistSortMode = persistSortMode;
        _layoutMode = initialLayout;
        _persistLayout = persistLayout;
        _isPresentationActive = isPresentationActive;
        _visibleNodeRows = new LazyRowList<string, ProxyNodeRowViewModel>((name, _) => CreateNodeRow(name));
        _emptyText = Localize("Proxy.Empty.NoGroups");
        if (_localization is not null)
        {
            _localization.LanguageChanged += OnLanguageChanged;
        }
        RefreshProxiesCommand = new RelayCommand(() => RefreshProxiesAsync().SafeFireAndForget("RefreshProxies"));
        SelectGroupCommand = new RelayCommand<string>(SelectGroup);
        SelectNodeCommand = new RelayCommand<string>(nodeName => SelectNodeAsync(nodeName).SafeFireAndForget("SelectNode"));
        TestNodeDelayCommand = new RelayCommand<string>(nodeName => TestNodeDelayAsync(nodeName).SafeFireAndForget("TestNodeDelay"));
        TestAllDelaysCommand = new RelayCommand(() => TestAllDelaysAsync().SafeFireAndForget("TestAllDelays"));
        TestCurrentGroupDelaysCommand = new RelayCommand(() => TestCurrentGroupDelaysAsync().SafeFireAndForget("TestCurrentGroupDelays"));
        LocateSelectedNodeCommand = new RelayCommand(LocateSelectedNode);
        ScrollToTopCommand = new RelayCommand(ScrollToTop);
        SetDefaultSortCommand = new RelayCommand(() => SetSortMode(ProxyNodeSortMode.Default));
        SetNameSortCommand = new RelayCommand(() => SetSortMode(ProxyNodeSortMode.Name));
        SetDelaySortCommand = new RelayCommand(() => SetSortMode(ProxyNodeSortMode.Delay));
        CycleSortModeCommand = new RelayCommand(CycleSortMode);
        ToggleLayoutCommand = new RelayCommand(ToggleLayout);
        ToggleGroupExpandCommand = new RelayCommand<string>(ToggleGroupExpand);
    }

    // 节点切换请求核心清理连接；组合层接入连接页同步。
    public event EventHandler? NodeSelectionClosedConnections;

    public IReadOnlyList<ProxyGroup> VisibleGroups => _visibleGroups;

    public IReadOnlyList<ProxyGroupButtonViewModel> VisibleGroupRows => _visibleGroupRows;

    public IReadOnlyList<ProxyGroupCardViewModel> VisibleGroupCards => _visibleGroupCards;

    public ProxyPageLayout LayoutMode => _layoutMode;

    public bool IsVerticalLayout => _layoutMode == ProxyPageLayout.Vertical;

    // 空状态、横向节点和纵向卡片互斥。
    public bool IsHorizontalContentVisible => !_isEmptyVisible && _layoutMode == ProxyPageLayout.Horizontal;

    public bool IsVerticalContentVisible => !_isEmptyVisible && _layoutMode == ProxyPageLayout.Vertical;

    public string? ExpandedGroupName => _expandedGroupName;

    // 切换图标指向备用布局，保持提示和动作一致。
    public string LayoutToggleIcon => _layoutMode == ProxyPageLayout.Vertical ? "LayoutGridLine" : "Rows4Line";

    public string LayoutToggleTooltip => Localize(_layoutMode == ProxyPageLayout.Vertical
        ? "Proxy.Tooltip.SwitchToGrid"
        : "Proxy.Tooltip.SwitchToList");

    public ProxyGroup? SelectedGroup => _selectedGroup;

    public IReadOnlyList<ProxyNodeRowViewModel> VisibleNodeRows => _visibleNodeRows;

    public int RealizedNodeRowCount => _visibleNodeRows.RealizedCount;

    public int IndexOfNode(string nodeName)
        => _visibleNodeIndexes.TryGetValue(nodeName, out var index) ? index : -1;

    public bool IsEmptyVisible => _isEmptyVisible;

    public bool HasGroups => VisibleGroups.Count > 0;

    public int? ParsedGroupCount => _hasLoadedConfig ? _config.Groups.Count : null;

    public int? ParsedNodeCount => _hasLoadedConfig ? _config.Nodes.Count : null;

    public bool IsInitialLoadCompleted => _isInitialLoadCompleted;

    public int? TestedAverageDelay
    {
        get
        {
            var delays = _delayTestedNodeNames
                .Select(name => _config.Nodes.TryGetValue(name, out var node) ? node.Delay : null)
                .Where(delay => delay >= 0)
                .Select(delay => delay!.Value)
                .ToArray();
            return delays.Length == 0 ? null : (int)Math.Round(delays.Average());
        }
    }

    public string? LoadedSubscriptionId => _loadedSubscriptionId;

    public ProxyRuntimeSnapshot? LastRuntimeSnapshot
        => (_primaryConfigProvider as IProxyRuntimeSnapshotSource)?.LastSnapshot;

    public string EmptyText => _emptyText;

    public string EmptySubtitle => _emptySubtitle;

    public string EmptyIconType => _emptyIconType;

    public Domain.Proxies.OutboundMode OutboundMode => _outboundMode;

    public bool IsCoreRunning => _isCoreRunning;

    public ProxyNodeSortMode SortMode => _sortMode;

    public string SortModeIcon => _sortMode switch
    {
        ProxyNodeSortMode.Name => "AzSortAscendingLettersLine",
        ProxyNodeSortMode.Delay => "StopwatchLine",
        _ => "ListOrderedLine",
    };

    public string SortModeTooltip => _sortMode switch
    {
        ProxyNodeSortMode.Name => Localize("Proxy.Sort.Name"),
        ProxyNodeSortMode.Delay => Localize("Proxy.Sort.Delay"),
        _ => Localize("Proxy.Sort.Default"),
    };

    public bool IsSortActive => _sortMode != ProxyNodeSortMode.Default;

    public string SearchKeyword
    {
        get => _searchKeyword;
        set
        {
            if (string.Equals(_searchKeyword, value, StringComparison.Ordinal))
            {
                return;
            }

            _searchKeyword = value;
            RaiseProxyStateChanged();
        }
    }

    public string? LastSelectedNodeName => _lastSelectedNodeName;

    public string? LocatedNodeName => _locatedNodeName;

    public int LocateNodeRequestId => _locateNodeRequestId;

    public ProxyChangeRequest? LastChangeRequest => _lastChangeRequest;

    public bool ShouldCloseConnectionsAfterSelection => _shouldCloseConnectionsAfterSelection;

    public bool HasScrolledToTop => _hasScrolledToTop;

    public int ScrollToTopRequestId => _scrollToTopRequestId;

    public IReadOnlyCollection<string> DelayTestedNodeNames => _delayTestedNodeNames;

    public IReadOnlyCollection<string> BatchDelayTestedNodeNames => _batchDelayTestedNodeNames;

    public IReadOnlyCollection<string> DelayTestingNodeNames => _delayTests.TestingNodeNames;

    public bool IsDelayTesting => _delayTests.IsTesting;

    public bool IsBatchDelayTesting => _delayTests.IsBatchTesting;

    public bool IsPresentationActive => _isPresentationActive;

    public ICommand RefreshProxiesCommand { get; }
    public ICommand SelectGroupCommand { get; }
    public ICommand SelectNodeCommand { get; }
    public ICommand TestNodeDelayCommand { get; }
    public ICommand TestAllDelaysCommand { get; }
    public ICommand TestCurrentGroupDelaysCommand { get; }
    public ICommand LocateSelectedNodeCommand { get; }
    public ICommand ScrollToTopCommand { get; }
    public ICommand SetDefaultSortCommand { get; }
    public ICommand SetNameSortCommand { get; }
    public ICommand SetDelaySortCommand { get; }
    public ICommand CycleSortModeCommand { get; }
    public ICommand ToggleLayoutCommand { get; }
    public ICommand ToggleGroupExpandCommand { get; }

    public void LoadConfig(
        ProxyConfig config,
        bool shouldChangeCoreOnSelection = true,
        bool shouldTestDelaysThroughService = true,
        string? subscriptionId = null)
    {
        CancelDelayTests();
        _hasLoadedConfig = true;
        _loadedSubscriptionId = subscriptionId;
        _configVersion++;
        var normalizedConfig = ProxyConfigSelectionNormalizer.EnsureManualSelections(config);
        // 运行时 YAML 模式由设置注入，所以静态值只是偏好。
        _outboundMode = normalizedConfig.Mode ?? _outboundMode;
        _config = normalizedConfig with { Mode = _outboundMode };
        if (_isPresentationActive)
        {
            RefreshConfigIndexes();
            _isPresentationCacheReleased = false;
        }
        else
        {
            _visibleGroups = _config.VisibleGroups;
            _entryNodes = new Dictionary<string, ProxyNode>(StringComparer.Ordinal);
            _isPresentationCacheReleased = true;
        }

        _selectedGroup = VisibleGroups.FirstOrDefault();
        _expandedGroupName = null;
        _searchKeyword = string.Empty;
        _delayTestedNodeNames.Clear();
        _batchDelayTestedNodeNames.Clear();
        _lastSelectedNodeName = null;
        _locatedNodeName = null;
        _locateNodeRequestId = 0;
        _hasScrolledToTop = false;
        _scrollToTopRequestId = 0;
        _lastChangeRequest = null;
        _shouldCloseConnectionsAfterSelection = false;
        _shouldChangeCoreOnSelection = shouldChangeCoreOnSelection;
        _shouldTestDelaysThroughService = shouldTestDelaysThroughService;
        RaiseProxyStateChanged();
        CompleteInitialLoad();
    }

    public void ActivatePresentation()
    {
        if (_isPresentationActive)
        {
            return;
        }

        if (_isPresentationCacheReleased)
        {
            WarmupPresentation();
        }

        _isPresentationActive = true;
        OnPropertyChanged(nameof(IsPresentationActive));
    }

    public void WarmupPresentation()
    {
        if (_isPresentationActive || !_isPresentationCacheReleased)
        {
            return;
        }

        RefreshConfigIndexes();
        _isPresentationCacheReleased = false;
        _isPresentationActive = true;
        RaiseProxyStateChanged();
        _isPresentationActive = false;
    }

    public void DeactivatePresentation()
    {
        if (!_isPresentationActive)
        {
            return;
        }

        _isPresentationActive = false;
        OnPropertyChanged(nameof(IsPresentationActive));
    }

    public void ReleasePresentationCache()
    {
        DeactivatePresentation();
        CancelDelayTests();
        _visibleGroupRows = [];
        _visibleGroupCards = [];
        _visibleNodeRows.Release();
        _visibleNodeNames = [];
        _visibleNodeIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        _entryNodes = new Dictionary<string, ProxyNode>(StringComparer.Ordinal);
        _isPresentationCacheReleased = true;
        OnPropertyChanged(nameof(VisibleGroupRows));
        OnPropertyChanged(nameof(VisibleGroupCards));
    }

    public void RefreshProxies()
    {
        RefreshProxiesAsync().SafeFireAndForget("RefreshProxies");
    }

    public void BindLoadedConfigToSubscription(string subscriptionId)
    {
        _loadedSubscriptionId = subscriptionId;
        OnPropertyChanged(nameof(LoadedSubscriptionId));
    }

    public async Task RefreshProxiesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            CancelDelayTests();

            if (_primaryConfigProvider is not null)
            {
                await LoadAsync(_primaryConfigProvider, _fallbackConfigProvider, cancellationToken);
            }
        }
        finally
        {
            CompleteInitialLoad();
        }
    }

    private void CompleteInitialLoad()
    {
        if (_isInitialLoadCompleted)
        {
            return;
        }

        _isInitialLoadCompleted = true;
        OnPropertyChanged(nameof(IsInitialLoadCompleted));
    }

    public void SetOutboundMode(Domain.Proxies.OutboundMode mode)
    {
        if (_outboundMode == mode && _config.Mode == mode)
        {
            return;
        }

        CancelDelayTests();
        // 出站模式会重建可见分组，所以按分组名恢复选择。
        var selectedGroupName = _selectedGroup?.Name;
        _outboundMode = mode;
        _config = _config with { Mode = mode };
        if (_isPresentationActive)
        {
            RefreshConfigIndexes();
        }
        else
        {
            _visibleGroups = _config.VisibleGroups;
            _isPresentationCacheReleased = true;
        }

        _selectedGroup = VisibleGroups.FirstOrDefault(group => string.Equals(group.Name, selectedGroupName, StringComparison.Ordinal))
            ?? VisibleGroups.FirstOrDefault();
        RaiseProxyStateChanged();
    }

    public void SetCoreRunning(bool isRunning)
    {
        if (_isCoreRunning == isRunning)
        {
            return;
        }

        _isCoreRunning = isRunning;
        RaiseProxyStateChanged();
    }

    public async Task LoadAsync(IProxyConfigProvider primary, IProxyConfigProvider? fallback, CancellationToken cancellationToken = default)
    {
        LoadConfig(await _resilientLoader.LoadAsync(primary, fallback, cancellationToken));
    }

    // 核心实时 API 不可用；外部选择同步循环据此退避。
    public bool IsRuntimeConfigDegraded => _resilientLoader.IsDegraded;

    public async Task SyncExternalSelectionsAsync(CancellationToken cancellationToken = default)
    {
        if (!_isPresentationActive
            || _primaryConfigProvider is null
            || _delayTests.IsTesting)
        {
            return;
        }

        if (Interlocked.Exchange(ref _externalSelectionSyncRunning, 1) == 1)
        {
            return;
        }

        try
        {
            var config = await _resilientLoader.LoadAsync(_primaryConfigProvider, _fallbackConfigProvider, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!_isPresentationActive || _delayTests.IsTesting)
            {
                return;
            }

            ApplySyncedConfig(config);
        }
        finally
        {
            Interlocked.Exchange(ref _externalSelectionSyncRunning, 0);
        }
    }

    public void SetSortMode(ProxyNodeSortMode mode)
    {
        if (_sortMode == mode)
        {
            return;
        }

        _sortMode = mode;
        _persistSortMode?.Invoke(mode);
        RaiseProxyStateChanged();
    }

    public void CycleSortMode()
    {
        SetSortMode(_sortMode switch
        {
            ProxyNodeSortMode.Default => ProxyNodeSortMode.Name,
            ProxyNodeSortMode.Name => ProxyNodeSortMode.Delay,
            _ => ProxyNodeSortMode.Default,
        });
    }

    public void ToggleLayout()
    {
        SetLayout(_layoutMode == ProxyPageLayout.Horizontal
            ? ProxyPageLayout.Vertical
            : ProxyPageLayout.Horizontal);
    }

    public void SetLayout(ProxyPageLayout layout)
    {
        if (_layoutMode == layout)
        {
            return;
        }

        _layoutMode = layout;
        _persistLayout?.Invoke(layout);
        RaiseProxyStateChanged();
    }

    // 纵向手风琴互斥，并为展开分组复用节点列表。
    public void ToggleGroupExpand(string? groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
        {
            return;
        }

        if (string.Equals(_expandedGroupName, groupName, StringComparison.Ordinal))
        {
            _expandedGroupName = null;
            RaiseProxyStateChanged();
            return;
        }

        var group = VisibleGroups.FirstOrDefault(item => string.Equals(item.Name, groupName, StringComparison.Ordinal));
        if (group is null)
        {
            return;
        }

        _expandedGroupName = groupName;
        _selectedGroup = group;
        RaiseProxyStateChanged();
    }

    public void SelectGroup(string? groupName)
    {
        var group = VisibleGroups.FirstOrDefault(item => string.Equals(item.Name, groupName, StringComparison.Ordinal));
        if (group is null)
        {
            return;
        }

        _selectedGroup = group;
        RaiseProxyStateChanged();
    }

    public void SelectNode(string? nodeName)
    {
        SelectNodeAsync(nodeName).SafeFireAndForget("SelectNode");
    }

    public async Task SelectNodeAsync(string? nodeName)
    {
        if (_selectedGroup is null || string.IsNullOrWhiteSpace(nodeName))
        {
            return;
        }

        if (!_selectedGroup.IsManualSelectable)
        {

            // 不支持核心切换的分组会忽略手动选择。
            AppLogger.Info($"Proxy group {_selectedGroup.Name} has type {_selectedGroup.Type} and cannot be switched manually");
            return;
        }

        var result = await _selectionService.SelectNodeAsync(_config, _selectedGroup.Name, nodeName, _shouldChangeCoreOnSelection);
        if (result is null)
        {
            return;
        }

        _config = result.Config;
        RefreshConfigIndexes();
        // 等待期间出站模式可能变化；回退处理，不假设仍可见。
        _selectedGroup = VisibleGroups.FirstOrDefault(group => string.Equals(group.Name, result.ChangeRequest.GroupName, StringComparison.Ordinal))
            ?? VisibleGroups.FirstOrDefault();
        _lastSelectedNodeName = nodeName;
        _lastChangeRequest = result.ChangeRequest;
        _shouldCloseConnectionsAfterSelection = result.ShouldCloseConnections;
        if (_shouldChangeCoreOnSelection && result.ShouldCloseConnections)
        {
            NodeSelectionClosedConnections?.Invoke(this, EventArgs.Empty);
        }

        RaiseProxyStateChanged();
    }

    public void TestNodeDelay(string? nodeName)
    {
        TestNodeDelayAsync(nodeName).SafeFireAndForget("TestNodeDelay");
    }

    public void TestGroupDelays(string? groupName)
    {
        TestGroupDelaysAsync(groupName).SafeFireAndForget("TestGroupDelays");
    }

    public void TestCurrentGroupDelays()
    {
        TestCurrentGroupDelaysAsync().SafeFireAndForget("TestCurrentGroupDelays");
    }

    public void TestAllDelays()
    {
        TestAllDelaysAsync().SafeFireAndForget("TestAllDelays");
    }

    public async Task TestAllDelaysForCurrentSubscriptionAsync()
    {
        await TestAllDelaysAsync();
    }

    public async Task TestNodeDelayAsync(string? nodeName)
    {
        if (string.IsNullOrWhiteSpace(nodeName)
            || _delayTests.IsBatchTarget(nodeName))
        {
            return;
        }

        var cancellation = BeginSingleDelayTest(nodeName);
        try
        {
            var configVersion = _configVersion;
            if (_shouldTestDelaysThroughService && _delayService is not null)
            {
                ProxyDelayResult result;
                try
                {
                    result = await _delayService.TestNodeAsync(_config, nodeName, cancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (IsStaleSingleDelayResult(nodeName, cancellation, configVersion))
                {
                    // 配置或令牌已变，过期延迟结果不能写回。
                    return;
                }

                ApplyDelayResult(result);
                _delayTestedNodeNames.UnionWith(result.TestedNodeNames);
                RaiseProxyStateChanged();
                return;
            }

            if (IsStaleSingleDelayResult(nodeName, cancellation, configVersion))
            {
                return;
            }

            var fallbackResult = ProxyDelayFallback.TestNodes(_config, [nodeName]);
            ApplyDelayResult(fallbackResult);
            _delayTestedNodeNames.UnionWith(fallbackResult.TestedNodeNames);
            RaiseProxyStateChanged();
        }
        finally
        {
            CompleteSingleDelayTest(nodeName, cancellation);
            cancellation.Dispose();
        }
    }

    public async Task TestAllDelaysAsync()
    {
        var nodeNames = AllGroupEntryNames();
        var excludedNodeNames = _delayTests.ActiveSingleNodeNames;
        await RunBatchDelayTestAsync(
            nodeNames,
            excludedNodeNames,
            (config, progress, token) => _delayService!.TestAllAsync(config, excludedNodeNames, progress, token),
            releaseGroupNames: null);
    }

    public async Task TestCurrentGroupDelaysAsync()
    {
        if (_selectedGroup is null)
        {
            return;
        }

        await TestGroupDelaysAsync(_selectedGroup.Name);
    }

    public async Task TestGroupDelaysAsync(string? groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
        {
            return;
        }

        var group = _config.Groups.FirstOrDefault(item => string.Equals(item.Name, groupName, StringComparison.Ordinal));
        if (group is null)
        {
            return;
        }

        var nodeNames = group.All;
        var excludedNodeNames = _delayTests.ActiveSingleNodeNames;
        await RunBatchDelayTestAsync(
            nodeNames,
            excludedNodeNames,
            (config, progress, token) => _delayService!.TestGroupAsync(config, group.Name, excludedNodeNames, progress, token),
            releaseGroupNames: [group.Name]);
    }

    // releaseGroupNames 为 null 表示解除全部分组的固定选择。
    private async Task RunBatchDelayTestAsync(
        IReadOnlyList<string> nodeNames,
        IReadOnlyList<string> excludedNodeNames,
        Func<ProxyConfig, IProgress<ProxyDelayProgress>, CancellationToken, Task<ProxyDelayResult>> serviceCall,
        IReadOnlyCollection<string>? releaseGroupNames)
    {
        if (nodeNames.Count == 0)
        {
            _batchDelayTestedNodeNames.Clear();
            _batchDelayTestedNodeNames.UnionWith(excludedNodeNames);
            RaiseProxyStateChanged();
            return;
        }

        var batchNodeNames = nodeNames.Except(excludedNodeNames, StringComparer.Ordinal).ToList();
        var cancellation = BeginBatchDelayTest(nodeNames);
        var configVersion = _configVersion;
        var progress = new Progress<ProxyDelayProgress>(item => OnDelayProgress(item, cancellation, configVersion));
        try
        {
            if (_shouldTestDelaysThroughService && _delayService is not null)
            {
                ProxyDelayResult result;
                try
                {
                    result = await serviceCall(_config, progress, cancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (IsStaleBatchDelayResult(cancellation, configVersion))
                {
                    return;
                }

                ApplyDelayResult(result);
                _delayTestedNodeNames.UnionWith(result.TestedNodeNames);
                _batchDelayTestedNodeNames.Clear();
                _batchDelayTestedNodeNames.UnionWith(
                    result.TestedNodeNames.Concat(result.SkippedNodeNames));
                await ReleaseFixedSelectionsAsync(releaseGroupNames, cancellation);
                RaiseProxyStateChanged();
                return;
            }

            if (IsStaleBatchDelayResult(cancellation, configVersion))
            {
                return;
            }

            var fallbackResult = ProxyDelayFallback.TestNodes(_config, batchNodeNames);
            ApplyDelayResult(fallbackResult);
            _delayTestedNodeNames.UnionWith(fallbackResult.TestedNodeNames);
            _batchDelayTestedNodeNames.Clear();
            _batchDelayTestedNodeNames.UnionWith(
                fallbackResult.TestedNodeNames.Concat(nodeNames.Intersect(excludedNodeNames, StringComparer.Ordinal)));
            await ReleaseFixedSelectionsAsync(releaseGroupNames, cancellation);
            RaiseProxyStateChanged();
        }
        finally
        {
            CompleteBatchDelayTest(cancellation);
            cancellation.Dispose();
        }
    }

    private async Task ReleaseFixedSelectionsAsync(
        IReadOnlyCollection<string>? groupNames,
        CancellationTokenSource cancellation)
    {
        ProxyFixedSelectionReleaseResult result;
        try
        {
            result = await _selectionService.ReleaseFixedSelectionsAsync(
                _config,
                groupNames,
                _shouldChangeCoreOnSelection,
                cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!result.HasChanges)
        {
            return;
        }

        // 按分组名改写当前配置：result.Config 是等待核心之前的快照，直接赋值会盖掉期间的改动。
        var releasedGroupNames = result.ReleasedGroupNames.ToHashSet(StringComparer.Ordinal);
        var groups = _config.Groups
            .Select(group => releasedGroupNames.Contains(group.Name) ? group with { Fixed = null } : group)
            .ToList();
        _config = _config with { Groups = groups };
        RefreshSelectedGroup();
    }

    private void OnDelayProgress(ProxyDelayProgress progress, CancellationTokenSource cancellation, int configVersion)
    {
        if (IsStaleBatchDelayResult(cancellation, configVersion))
        {
            return;
        }

        if (!progress.IsCompleted)
        {
            _delayTests.MarkBatchNodeTesting(progress.ProxyName);
            if (_visibleNodeIndexes.TryGetValue(progress.ProxyName, out var testingIndex)
                && _visibleNodeRows.GetRealizedRow(testingIndex) is { } testingRow)
            {
                testingRow.SetDelayTesting(true);
            }

            return;
        }

        // 批量进度原地更新行；最终排序只刷新一次。
        _delayTests.MarkBatchNodeCompleted(progress.ProxyName, progress.Delay);
        _delayTestedNodeNames.Add(progress.ProxyName);
        if (_visibleNodeIndexes.TryGetValue(progress.ProxyName, out var index)
            && _visibleNodeRows.GetRealizedRow(index) is { } row)
        {
            row.ApplyDelay(progress.Delay);
        }
    }

    public void LocateSelectedNode()
    {
        var nodeName = _selectedGroup?.DisplaySelectionName;
        if (string.IsNullOrWhiteSpace(nodeName))
        {
            return;
        }

        _locatedNodeName = nodeName;
        _locateNodeRequestId++;
        RaiseProxyStateChanged();
    }

    private IReadOnlyList<string> AllGroupEntryNames()
    {
        return _config.Groups
            .SelectMany(group => group.All)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public void ScrollToTop()
    {
        _hasScrolledToTop = true;
        _scrollToTopRequestId++;
        RaiseProxyStateChanged();
    }

    public void CancelDelayTests()
    {
        _delayTests.CancelAll();
        RaiseProxyStateChanged();
    }

    private void RefreshSelectedGroup()
    {
        if (_selectedGroup is null)
        {
            return;
        }

        RefreshConfigIndexes();
        _selectedGroup = VisibleGroups.FirstOrDefault(group => string.Equals(group.Name, _selectedGroup.Name, StringComparison.Ordinal));
    }

    private void ApplySyncedConfig(ProxyConfig config)
    {
        var selectedGroupName = _selectedGroup?.Name;
        var currentDelays = CurrentEntryDelays();
        var normalizedConfig = ProxyConfigSelectionNormalizer.EnsureManualSelections(config)
            .WithEntryDelays(currentDelays);
        var nextMode = normalizedConfig.Mode ?? _outboundMode;
        var nextConfig = normalizedConfig with { Mode = nextMode };

        // 选中态与分组结构未变时跳过全量刷新，避免每 2 s 重建界面模型。
        if (AreProxySelectionsEquivalent(_config, nextConfig)
            && _outboundMode == nextMode)
        {
            return;
        }

        _configVersion++;
        _outboundMode = nextMode;
        _config = nextConfig;
        RefreshConfigIndexes();
        _selectedGroup = VisibleGroups.FirstOrDefault(group => string.Equals(group.Name, selectedGroupName, StringComparison.Ordinal))
            ?? VisibleGroups.FirstOrDefault();
        RaiseProxyStateChanged();
    }

    private static bool AreProxySelectionsEquivalent(ProxyConfig left, ProxyConfig right)
    {
        if (!Nullable.Equals(left.Mode, right.Mode)
            || left.Groups.Count != right.Groups.Count
            || left.Nodes.Count != right.Nodes.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Groups.Count; index++)
        {
            var leftGroup = left.Groups[index];
            var rightGroup = right.Groups[index];
            if (!string.Equals(leftGroup.Name, rightGroup.Name, StringComparison.Ordinal)
                || !string.Equals(leftGroup.Type, rightGroup.Type, StringComparison.Ordinal)
                || !string.Equals(leftGroup.Now, rightGroup.Now, StringComparison.Ordinal)
                || !string.Equals(leftGroup.Fixed, rightGroup.Fixed, StringComparison.Ordinal)
                || leftGroup.IsHidden != rightGroup.IsHidden
                || leftGroup.All.Count != rightGroup.All.Count)
            {
                return false;
            }

            for (var nodeIndex = 0; nodeIndex < leftGroup.All.Count; nodeIndex++)
            {
                if (!string.Equals(leftGroup.All[nodeIndex], rightGroup.All[nodeIndex], StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }

        return true;
    }

    // 外部同步只更新核心选择，当前会话的延迟测试结果由代理页持有。
    private IReadOnlyDictionary<string, int> CurrentEntryDelays()
    {
        var delays = _config.Nodes
            .Where(item => item.Value.Delay is not null)
            .ToDictionary(item => item.Key, item => item.Value.Delay!.Value, StringComparer.Ordinal);
        foreach (var group in _config.Groups.Where(group => group.Delay is not null))
        {
            delays[group.Name] = group.Delay!.Value;
        }

        return delays;
    }

    private void RaiseProxyStateChanged()
    {
        if (_isPresentationActive)
        {
            RefreshVisibleRows();
            _isPresentationCacheReleased = false;
        }
        else
        {
            _isPresentationCacheReleased = true;
        }

        OnPropertyChanged(nameof(VisibleGroups));
        OnPropertyChanged(nameof(SelectedGroup));
        OnPropertyChanged(nameof(IsEmptyVisible));
        OnPropertyChanged(nameof(HasGroups));
        OnPropertyChanged(nameof(ParsedGroupCount));
        OnPropertyChanged(nameof(ParsedNodeCount));
        OnPropertyChanged(nameof(TestedAverageDelay));
        OnPropertyChanged(nameof(LoadedSubscriptionId));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(EmptySubtitle));
        OnPropertyChanged(nameof(EmptyIconType));
        OnPropertyChanged(nameof(OutboundMode));
        OnPropertyChanged(nameof(IsCoreRunning));
        OnPropertyChanged(nameof(SortMode));
        OnPropertyChanged(nameof(SortModeIcon));
        OnPropertyChanged(nameof(SortModeTooltip));
        OnPropertyChanged(nameof(IsSortActive));
        OnPropertyChanged(nameof(SearchKeyword));
        OnPropertyChanged(nameof(LastSelectedNodeName));
        OnPropertyChanged(nameof(LocatedNodeName));
        OnPropertyChanged(nameof(LocateNodeRequestId));
        OnPropertyChanged(nameof(LastChangeRequest));
        OnPropertyChanged(nameof(ShouldCloseConnectionsAfterSelection));
        OnPropertyChanged(nameof(HasScrolledToTop));
        OnPropertyChanged(nameof(ScrollToTopRequestId));
        OnPropertyChanged(nameof(DelayTestedNodeNames));
        OnPropertyChanged(nameof(BatchDelayTestedNodeNames));
        OnPropertyChanged(nameof(DelayTestingNodeNames));
        OnPropertyChanged(nameof(IsDelayTesting));
        OnPropertyChanged(nameof(IsBatchDelayTesting));
        OnPropertyChanged(nameof(LayoutMode));
        OnPropertyChanged(nameof(IsVerticalLayout));
        OnPropertyChanged(nameof(IsHorizontalContentVisible));
        OnPropertyChanged(nameof(IsVerticalContentVisible));
        OnPropertyChanged(nameof(ExpandedGroupName));
        OnPropertyChanged(nameof(LayoutToggleIcon));
        OnPropertyChanged(nameof(LayoutToggleTooltip));
    }

    private void RefreshConfigIndexes()
    {
        var visibleConfig = _config.WithEntryDelays(_delayTests.BatchResults);
        _visibleGroups = visibleConfig.VisibleGroups;
        var entryNodes = new Dictionary<string, ProxyNode>(visibleConfig.Nodes, StringComparer.Ordinal);
        foreach (var group in visibleConfig.Groups)
        {
            visibleConfig.TryGetResolvedEntryDelay(group.Name, out var delay);
            entryNodes.TryAdd(group.Name, new ProxyNode(group.Name, group.Type, delay));
        }

        _entryNodes = entryNodes;
    }

    private void RefreshVisibleRows()
    {
        if (_expandedGroupName is not null
            && !VisibleGroups.Any(group => string.Equals(group.Name, _expandedGroupName, StringComparison.Ordinal)))
        {
            _expandedGroupName = null;
        }

        SyncGroupRows();
        SyncGroupCards();

        if (_selectedGroup is null)
        {
            if (_visibleNodeRows.Count > 0)
            {
                _visibleNodeNames = [];
                _visibleNodeIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
                _visibleNodeRows.Replace([]);
            }
        }
        else
        {
            var orderedNames = _sorter.FilterAndSort(_selectedGroup.All, _entryNodes, _sortMode, _searchKeyword)
                .Where(name => _entryNodes.ContainsKey(name))
                .ToList();

            if (NodeRowsMatch(orderedNames))
            {
                // 节点顺序稳定时复用行，保留滚动和动画状态。
                _visibleNodeRows.UpdateInPlace(
                    orderedNames,
                    (row, name, _) => UpdateNodeRow(row, name, _selectedGroup));
            }
            else
            {
                _visibleNodeNames = orderedNames;
                _visibleNodeIndexes = orderedNames
                    .Select((name, index) => (name, index))
                    .ToDictionary(item => item.name, item => item.index, StringComparer.Ordinal);
                _visibleNodeRows.Replace(orderedNames);
            }
        }

        _isEmptyVisible = _layoutMode == ProxyPageLayout.Vertical
            ? VisibleGroups.Count == 0
            : _selectedGroup is null || _visibleNodeRows.Count == 0;

        // Direct 模式隐藏代理分组，避免展示无效选择。
        if (_isCoreRunning && _outboundMode == Domain.Proxies.OutboundMode.Direct && VisibleGroups.Count == 0)
        {
            _emptyText = Localize("Proxy.Empty.DirectMode");
            _emptySubtitle = Localize("Proxy.Empty.DirectModeDescription");
            _emptyIconType = "SendPlaneLine";
        }
        else if (VisibleGroups.Count == 0)
        {
            _emptyText = Localize("Proxy.Empty.NoGroups");
            _emptySubtitle = string.Empty;
            _emptyIconType = "EarthLine";
        }
        else
        {
            _emptyText = Localize("Proxy.Empty.NoMatches");
            _emptySubtitle = string.Empty;
            _emptyIconType = "EarthLine";
        }
    }

    private bool NodeRowsMatch(IReadOnlyList<string> orderedNames)
    {
        if (_visibleNodeNames.Count != orderedNames.Count)
        {
            return false;
        }

        for (var index = 0; index < orderedNames.Count; index++)
        {
            if (!string.Equals(_visibleNodeNames[index], orderedNames[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private ProxyNodeRowViewModel CreateNodeRow(string name)
    {
        var row = new ProxyNodeRowViewModel(
            _entryNodes[name],
            string.Equals(name, _selectedGroup?.DisplaySelectionName, StringComparison.Ordinal),
            string.Equals(name, _locatedNodeName, StringComparison.Ordinal),
            _selectedGroup?.IsManualSelectable == true,
            _delayTests.TestingNodeNames.Contains(name));
        ApplyPendingBatchDelay(row, name);
        return row;
    }

    private void UpdateNodeRow(ProxyNodeRowViewModel row, string name, ProxyGroup? group)
    {
        row.Update(
            _entryNodes[name],
            string.Equals(name, group?.DisplaySelectionName, StringComparison.Ordinal),
            string.Equals(name, _locatedNodeName, StringComparison.Ordinal),
            group?.IsManualSelectable == true,
            _delayTests.TestingNodeNames.Contains(name));
        ApplyPendingBatchDelay(row, name);
    }

    // _entryNodes 仅在 RefreshConfigIndexes 时并入批测结果，滞后期不补写会让已测完的节点显示回退。
    private void ApplyPendingBatchDelay(ProxyNodeRowViewModel row, string name)
    {
        if (_delayTests.BatchResults.TryGetValue(name, out var delay))
        {
            row.ApplyDelay(delay);
        }
    }

    private void SyncGroupRows()
    {
        var selectedName = _selectedGroup?.Name;
        if (GroupRowsMatchCurrentGroups())
        {
            // 顺序稳定时复用行实例，同时替换分组快照。
            for (var index = 0; index < _visibleGroupRows.Count; index++)
            {
                var group = VisibleGroups[index];
                _visibleGroupRows[index].Update(group, string.Equals(group.Name, selectedName, StringComparison.Ordinal));
            }

            return;
        }

        _visibleGroupRows = VisibleGroups
            .Select(group => new ProxyGroupButtonViewModel(
                group,
                string.Equals(group.Name, selectedName, StringComparison.Ordinal)))
            .ToList();
        OnPropertyChanged(nameof(VisibleGroupRows));
    }

    private bool GroupRowsMatchCurrentGroups()
    {
        if (_visibleGroupRows.Count != VisibleGroups.Count)
        {
            return false;
        }

        for (var index = 0; index < _visibleGroupRows.Count; index++)
        {
            if (!string.Equals(_visibleGroupRows[index].Name, VisibleGroups[index].Name, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private void SyncGroupCards()
    {
        if (GroupCardsMatchCurrentGroups())
        {
            // 顺序稳定时复用卡片实例，同时替换快照。
            for (var index = 0; index < _visibleGroupCards.Count; index++)
            {
                var group = VisibleGroups[index];
                _visibleGroupCards[index].Update(
                    group,
                    FriendlySelectionName(group),
                    NodeCountLabel(group.All.Count),
                    string.Equals(group.Name, _expandedGroupName, StringComparison.Ordinal));
            }

            return;
        }

        _visibleGroupCards = VisibleGroups
            .Select(group => new ProxyGroupCardViewModel(
                group,
                FriendlySelectionName(group),
                NodeCountLabel(group.All.Count),
                string.Equals(group.Name, _expandedGroupName, StringComparison.Ordinal)))
            .ToList();
        OnPropertyChanged(nameof(VisibleGroupCards));
    }

    private bool GroupCardsMatchCurrentGroups()
    {
        if (_visibleGroupCards.Count != VisibleGroups.Count)
        {
            return false;
        }

        for (var index = 0; index < _visibleGroupCards.Count; index++)
        {
            if (!string.Equals(_visibleGroupCards[index].Name, VisibleGroups[index].Name, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private string NodeCountLabel(int count) => string.Format(
        System.Globalization.CultureInfo.CurrentCulture,
        Localize("Proxy.Group.NodeCount"),
        count);

    // 内置出站使用本地化显示名；自定义名称保持不变。
    private string FriendlySelectionName(ProxyGroup group)
    {
        var name = group.DisplaySelectionName ?? string.Empty;
        return name switch
        {
            "DIRECT" => Localize("Proxy.Selection.Direct"),
            "REJECT" or "REJECT-DROP" => Localize("Proxy.Selection.Reject"),
            _ => name,
        };
    }

    private CancellationTokenSource BeginSingleDelayTest(string nodeName)
    {
        var cancellation = _delayTests.BeginSingle(nodeName);
        RaiseProxyStateChanged();
        return cancellation;
    }

    private CancellationTokenSource BeginBatchDelayTest(IReadOnlyList<string> targetNodeNames)
    {
        var cancellation = _delayTests.BeginBatch(targetNodeNames);
        RaiseProxyStateChanged();
        return cancellation;
    }

    // 配置换代后旧结果也算过期：延迟对应的是另一份节点表。
    private bool IsStaleSingleDelayResult(
        string nodeName,
        CancellationTokenSource cancellation,
        int configVersion)
    {
        return !_delayTests.IsSingleCurrent(nodeName, cancellation)
            || _configVersion != configVersion;
    }

    private bool IsStaleBatchDelayResult(CancellationTokenSource cancellation, int configVersion)
    {
        return !_delayTests.IsBatchCurrent(cancellation)
            || _configVersion != configVersion;
    }

    private void CompleteSingleDelayTest(string nodeName, CancellationTokenSource cancellation)
    {
        _delayTests.CompleteSingle(nodeName, cancellation);
        RaiseProxyStateChanged();
    }

    private void CompleteBatchDelayTest(CancellationTokenSource cancellation)
    {
        _delayTests.CompleteBatch(cancellation);
        RaiseProxyStateChanged();
    }

    private void ApplyDelayResult(ProxyDelayResult result)
    {
        var delays = result.TestedNodeNames
            .Where(name => result.Config.TryGetEntryDelay(name, out _))
            .ToDictionary(
                name => name,
                name =>
                {
                    result.Config.TryGetEntryDelay(name, out var delay);
                    return delay ?? -1;
                },
                StringComparer.Ordinal);
        _config = _config.WithEntryDelays(delays).WithEntryDelays(_delayTests.BatchResults);
        RefreshSelectedGroup();
    }

    public void ApplyBackgroundDelays(string? subscriptionId, IReadOnlyDictionary<string, int> delays)
    {
        if (subscriptionId is null || subscriptionId != _loadedSubscriptionId || delays.Count == 0)
        {
            return;
        }
        _config = _config.WithEntryDelays(delays).WithEntryDelays(_delayTests.BatchResults);
        RefreshSelectedGroup();
        RaiseProxyStateChanged();
    }

    public void Dispose()
    {
        if (_localization is not null)
        {
            _localization.LanguageChanged -= OnLanguageChanged;
        }

        CancelDelayTests();
    }

    private void OnLanguageChanged(object? sender, EventArgs args)
    {
        _visibleNodeRows.Release();
        _visibleNodeNames = [];
        _visibleNodeIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        _visibleGroupRows = [];
        _visibleGroupCards = [];
        _isPresentationCacheReleased = true;
        RaiseProxyStateChanged();
    }

    private string Localize(string key) => _localization?.GetString(key) ?? key;
}
