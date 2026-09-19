using System.Windows.Input;
using Stelliberty.Application.Connections;
using Stelliberty.Domain.Connections;
using Stelliberty.Application.Localization;
using Stelliberty.Application.Proxies;
using Stelliberty.Domain.Proxies;
using Stelliberty.Presentation.Commands;
using Stelliberty.Presentation.Formatting;

namespace Stelliberty.Presentation.ViewModels;

public sealed class ConnectionPageViewModel : ViewModelBase, IDisposable
{
    private readonly DialogCloseResetScheduler _detailCloseReset = new();
    private readonly ConnectionFilter _filter = new();
    private readonly ConnectionListReducer _reducer = new();
    private readonly IProxyCoreClient? _coreClient;
    private readonly ILocalizationService? _localization;
    private readonly Func<DateTimeOffset> _now;
    private ConnectionListState _state = ConnectionListState.Initial;
    private ConnectionFilterLevel _filterLevel = ConnectionFilterLevel.All;
    private string _searchKeyword = string.Empty;
    private ConnectionInfo? _selectedConnection;
    private bool _isDetailVisible;
    private bool _hasClosedAllConnections;
    private bool _isRefreshing;
    private const int MaxClosedConnectionIds = 500;
    private readonly List<string> _closedConnectionIds = [];
    private int _directConnectionCount;
    private long _totalUploadSpeed;
    private long _totalDownloadSpeed;
    private CoreTrafficRate? _latestTrafficRate;
    private IReadOnlyList<ConnectionDetailGroupViewModel> _detailGroups = [];
    private IReadOnlyList<string> _detailLines = [];
    private string _detailText = string.Empty;
    private IReadOnlyList<ConnectionInfo> _filteredConnections = [];
    private List<ConnectionRowViewModel> _visibleConnectionRows = [];
    private string _detailSummaryChain = string.Empty;
    private string _detailSummaryUpload = string.Empty;
    private string _detailSummaryDownload = string.Empty;
    private string _detailSummaryDuration = string.Empty;
    private string _detailSummaryRule = string.Empty;

    public ConnectionPageViewModel(IProxyCoreClient? coreClient = null, Func<DateTimeOffset>? now = null, ILocalizationService? localization = null)
    {
        _coreClient = coreClient;
        _localization = localization;
        _now = now ?? (() => DateTimeOffset.Now);
        if (_localization is not null)
        {
            _localization.LanguageChanged += OnLanguageChanged;
        }
        ShowAllConnectionsCommand = new RelayCommand(() => SetFilterLevel(ConnectionFilterLevel.All));
        ShowDirectConnectionsCommand = new RelayCommand(() => SetFilterLevel(ConnectionFilterLevel.Direct));
        ShowProxyConnectionsCommand = new RelayCommand(() => SetFilterLevel(ConnectionFilterLevel.Proxy));
        RefreshConnectionsCommand = new RelayCommand(() => RefreshConnectionsAsync().SafeFireAndForget("RefreshConnections"));
        TogglePauseCommand = new RelayCommand(TogglePause);
        CloseConnectionCommand = new RelayCommand<string>(connectionId => CloseConnectionAsync(connectionId).SafeFireAndForget("CloseConnection"));
        CloseAllConnectionsCommand = new RelayCommand(() => CloseAllConnectionsAsync().SafeFireAndForget("CloseAllConnections"));
        ShowDetailCommand = new RelayCommand<string>(ShowDetail);
        CloseDetailCommand = new RelayCommand(CloseDetail);
    }

    public PageLoadingState Loading { get; } = new();
    public IReadOnlyList<ConnectionInfo> Connections => _state.Connections;

    public IReadOnlyList<ConnectionInfo> FilteredConnections => _filteredConnections;

    public IReadOnlyList<ConnectionRowViewModel> FilteredConnectionRows => _visibleConnectionRows;

    public int TotalConnectionCount => Connections.Count;

    public int FilteredConnectionCount => _filteredConnections.Count;

    public int DirectConnectionCount => _directConnectionCount;

    public int ProxyConnectionCount => Connections.Count - _directConnectionCount;

    public string TotalUploadSpeedText => $"{ByteSize.Format(_totalUploadSpeed)}/s";

    public string TotalDownloadSpeedText => $"{ByteSize.Format(_totalDownloadSpeed)}/s";

    public bool IsMonitoringPaused => _state.IsMonitoringPaused;

    public string MonitoringToggleTooltip => Localize(IsMonitoringPaused
        ? "Connections.Action.Resume"
        : "Connections.Action.Pause");

    public ConnectionFilterLevel FilterLevel => _filterLevel;

    public bool IsAllConnectionsSelected => _filterLevel == ConnectionFilterLevel.All;
    public bool IsDirectConnectionsSelected => _filterLevel == ConnectionFilterLevel.Direct;
    public bool IsProxyConnectionsSelected => _filterLevel == ConnectionFilterLevel.Proxy;

    public bool IsEmptyVisible => _filteredConnections.Count == 0;

    public string EmptyText => Connections.Count == 0
        ? Localize("Connections.Empty.NoConnections")
        : Localize("Connections.Empty.NoMatches");

    public ConnectionInfo? SelectedConnection => _selectedConnection;

    public bool IsDetailVisible => _isDetailVisible;

    public IReadOnlyList<string> SelectedConnectionDetailLines => _detailLines;

    public string SelectedConnectionDetailText => _detailText;

    public IReadOnlyList<ConnectionDetailGroupViewModel> SelectedConnectionDetailGroups => _detailGroups;

    public string DetailSummaryChain => _detailSummaryChain;
    public string DetailSummaryUpload => _detailSummaryUpload;
    public string DetailSummaryDownload => _detailSummaryDownload;
    public string DetailSummaryDuration => _detailSummaryDuration;
    public string DetailSummaryRule => _detailSummaryRule;

    public bool HasClosedAllConnections => _hasClosedAllConnections;

    public IReadOnlyList<string> ClosedConnectionIds => _closedConnectionIds;

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
            RaiseConnectionStateChanged();
        }
    }

    public ICommand ShowAllConnectionsCommand { get; }
    public ICommand ShowDirectConnectionsCommand { get; }
    public ICommand ShowProxyConnectionsCommand { get; }
    public ICommand RefreshConnectionsCommand { get; }
    public ICommand TogglePauseCommand { get; }
    public ICommand CloseConnectionCommand { get; }
    public ICommand CloseAllConnectionsCommand { get; }
    public ICommand ShowDetailCommand { get; }
    public ICommand CloseDetailCommand { get; }

    public void ApplyIncoming(IReadOnlyList<ConnectionInfo> connections, CoreTrafficRate? trafficRate = null, bool updateTrafficRate = true)
    {
        var wasPaused = _state.IsMonitoringPaused;
        _state = _reducer.ApplyIncoming(_state, connections, _now());
        if (!wasPaused && updateTrafficRate)
        {
            _latestTrafficRate = trafficRate;
        }

        SyncSelectedConnectionDetail();
        RaiseConnectionStateChanged();
        Loading.CompleteInitialLoad();
    }

    public async Task RefreshConnectionsAsync(CancellationToken cancellationToken = default)
    {
        // 单在途互斥：上一轮未归时跳过本轮，避免请求堆积与旧结果乱序覆盖
        if (_isRefreshing)
        {
            return;
        }

        // 周期轮询保留已有内容，只为首次快照显示加载状态。
        using var loading = Loading.HasLoaded ? null : Loading.BeginLoading();
        if (_coreClient is null) return;
        _isRefreshing = true;
        try
        {
            var connections = await _coreClient.GetConnectionsAsync(cancellationToken);
            // 读取失败保留现有列表，不显示成"没有连接"
            if (connections is null)
            {
                return;
            }

            ApplyIncoming(connections, updateTrafficRate: false);
            ApplyTrafficRate(await _coreClient.GetTrafficAsync(cancellationToken));
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void ApplyTrafficRate(CoreTrafficRate? trafficRate)
    {
        if (trafficRate is null || _state.IsMonitoringPaused)
        {
            return;
        }

        _latestTrafficRate = trafficRate;
        RaiseTotalSpeedChanged();
    }

    public void SetFilterLevel(ConnectionFilterLevel level)
    {
        if (_filterLevel == level)
        {
            return;
        }

        _filterLevel = level;
        RaiseConnectionStateChanged();
    }

    public void TogglePause()
    {
        _state = _reducer.TogglePause(_state);
        RaiseConnectionStateChanged();
    }

    public void CloseConnection(string? connectionId)
    {
        CloseConnectionAsync(connectionId).SafeFireAndForget("CloseConnection");
    }

    public void CloseAllConnections()
    {
        CloseAllConnectionsAsync().SafeFireAndForget("CloseAllConnections");
    }

    public async Task CloseConnectionAsync(string? connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
        {
            return;
        }

        if (Connections.All(connection => connection.Id != connectionId))
        {
            return;
        }

        var result = new ConnectionOperations(_state.Connections).CloseConnection(connectionId, _state.IsMonitoringPaused);
        if (_coreClient is not null && !await _coreClient.CloseConnectionsAsync(result.Request))
        {
            return;
        }

        _closedConnectionIds.Add(connectionId);
        if (_closedConnectionIds.Count > MaxClosedConnectionIds)
        {
            _closedConnectionIds.RemoveAt(0);
        }

        _state = result.State;
        CloseDetailWhenSelectedConnectionIsMissing();
        RaiseConnectionStateChanged();
    }

    public async Task CloseAllConnectionsAsync()
    {
        var result = new ConnectionOperations(_state.Connections).CloseAllConnections(_state.IsMonitoringPaused);
        if (_coreClient is not null && !await _coreClient.CloseConnectionsAsync(result.Request))
        {
            return;
        }

        _hasClosedAllConnections = true;
        _state = result.State;
        CloseDetailWhenSelectedConnectionIsMissing();
        RaiseConnectionStateChanged();
    }

    // 外部核心操作可能关闭全部连接；同步本地状态时不再二次调用核心。
    public void ApplyAllConnectionsClosed()
    {
        var result = new ConnectionOperations(_state.Connections).CloseAllConnections(_state.IsMonitoringPaused);
        _hasClosedAllConnections = true;
        _state = result.State;
        CloseDetailWhenSelectedConnectionIsMissing();
        RaiseConnectionStateChanged();
    }

    public void ShowDetail(string? connectionId)
    {
        var connection = Connections.FirstOrDefault(connection => connection.Id == connectionId);
        if (connection is null)
        {
            BeginCloseDetail();
            return;
        }

        _detailCloseReset.Cancel();
        _selectedConnection = connection;
        _isDetailVisible = true;
        RaiseDetailStateChanged();
    }

    public void CloseDetail()
    {
        BeginCloseDetail();
    }

    private void CloseDetailWhenSelectedConnectionIsMissing()
    {
        if (_selectedConnection is null || Connections.Any(connection => connection.Id == _selectedConnection.Id))
        {
            return;
        }

        BeginCloseDetail();
    }

    private void SyncSelectedConnectionDetail()
    {
        if (_selectedConnection is null || !_isDetailVisible)
        {
            return;
        }

        var currentConnection = Connections.FirstOrDefault(connection => connection.Id == _selectedConnection.Id);
        if (currentConnection is null)
        {
            BeginCloseDetail();
            return;
        }

        _selectedConnection = currentConnection;
        RaiseDetailStateChanged();
    }

    private void BeginCloseDetail()
    {
        if (!_isDetailVisible)
        {
            return;
        }

        _isDetailVisible = false;
        OnPropertyChanged(nameof(IsDetailVisible));
        _detailCloseReset.Run(() => !_isDetailVisible, ResetDetail);
    }

    private void ResetDetail()
    {
        _selectedConnection = null;
        RaiseDetailStateChanged();
    }

    private IReadOnlyList<string> BuildDetailLinesFromGroups(IReadOnlyList<ConnectionDetailGroupViewModel> groups)
    {
        var detailLines = groups.SelectMany(group => group.Rows.Select(row => $"{row.Label}：{row.Value}")).ToList();
        var description = groups.SelectMany(group => group.Rows).FirstOrDefault(row => row.Label == Localize("Connections.Detail.Description"))?.Value;
        if (!string.IsNullOrWhiteSpace(description))
        {
            detailLines.Insert(0, description);
        }

        return detailLines;
    }

    private string BuildDetailTextFromGroups(IReadOnlyList<ConnectionDetailGroupViewModel> groups)
    {
        var lines = new List<string> { Localize("Connections.Detail.Title") };
        foreach (var group in groups)
        {
            lines.Add(group.Title);
            lines.AddRange(group.Rows.Select(row => $"{row.Label}：{row.Value}"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private IReadOnlyList<ConnectionDetailGroupViewModel> BuildDetailGroups(ConnectionInfo? connection)
    {
        if (connection is null)
        {
            return [];
        }

        var metadata = connection.Metadata;
        var row = new ConnectionRowViewModel(connection, _now());
        var sourceAddress = FormatEndpoint(metadata.SourceIp, metadata.SourcePort);
        var destinationAddress = FormatEndpoint(
            string.IsNullOrWhiteSpace(metadata.DestinationIp) ? metadata.Host : metadata.DestinationIp,
            metadata.DestinationPort);
        var inboundAddress = FormatEndpoint(metadata.InboundIp, metadata.InboundPort);
        var description = string.IsNullOrWhiteSpace(metadata.Description)
            ? $"{metadata.Network}://{metadata.Host}:{metadata.DestinationPort}"
            : metadata.Description;
        var ruleText = $"{connection.Rule} {connection.RulePayload}".Trim();
        var chainText = string.Join(" / ", connection.Chains);

        var groups = new[]
        {
            Group(Localize("Connections.Detail.Group.Basic"), [
                Row(Localize("Connections.Detail.ConnectionId"), connection.Id, "mono"),
                Row(Localize("Connections.Detail.Description"), description),
                Row(Localize("Connections.Detail.Type"), metadata.Type),
                Row(Localize("Connections.Detail.Network"), metadata.Network.ToUpperInvariant())
            ]),
            Group(Localize("Connections.Detail.Group.Address"), [
                Row(Localize("Connections.Detail.Source"), sourceAddress, "mono"),
                Row(Localize("Connections.Detail.SourceGeoIp"), string.Join(", ", metadata.SourceGeoIp)),
                Row(Localize("Connections.Detail.SourceAsn"), metadata.SourceIpAsn, "muted"),
                Row(Localize("Connections.Detail.Destination"), destinationAddress, "mono"),
                Row(Localize("Connections.Detail.DestinationGeoIp"), string.Join(", ", metadata.DestinationGeoIp)),
                Row(Localize("Connections.Detail.DestinationAsn"), metadata.DestinationIpAsn, "muted"),
                Row(Localize("Connections.Detail.RemoteDestination"), metadata.RemoteDestination, "mono"),
                Row(Localize("Connections.Detail.Host"), metadata.Host, "mono"),
                Row(Localize("Connections.Detail.SniffHost"), metadata.SniffHost, "accent")
            ]),
            Group(Localize("Connections.Detail.Group.Inbound"), [
                Row(Localize("Connections.Detail.InboundName"), metadata.InboundName),
                Row(Localize("Connections.Detail.InboundAddress"), inboundAddress, "mono"),
                Row(Localize("Connections.Detail.InboundUser"), metadata.InboundUser)
            ]),
            Group(Localize("Connections.Detail.Group.Process"), [
                Row(Localize("Connections.Card.Process"), metadata.Process, "accent"),
                Row(Localize("Connections.Detail.Path"), metadata.ProcessPath, "mono"),
                // UID 仅 Unix 有意义，Windows 核心恒返回 0，置空由 Group 过滤
                Row("UID", OperatingSystem.IsWindows() ? string.Empty : metadata.Uid?.ToString() ?? string.Empty, "mono")
            ]),
            Group(Localize("Connections.Detail.Group.Rule"), [
                Row(Localize("Connections.Card.Rule"), ruleText),
                Row(Localize("Connections.Detail.ProxyGroup"), connection.ProxyGroup),
                Row(Localize("Connections.Detail.ProxyNode"), connection.ProxyNode, "accent"),
                Row(Localize("Connections.Card.Chain"), chainText, "accent"),
                Row(Localize("Connections.Detail.ProxyChain"), connection.LegacyProxyChain, "muted")
            ]),
            Group(Localize("Connections.Detail.Group.Advanced"), [
                Row("DSCP", metadata.Dscp == 0 ? string.Empty : metadata.Dscp.ToString(), "mono"),
                Row(Localize("Connections.Detail.DnsMode"), metadata.DnsMode),
                Row(Localize("Connections.Detail.SpecialProxy"), metadata.SpecialProxy),
                Row(Localize("Connections.Detail.SpecialRules"), metadata.SpecialRules)
            ]),
            Group(Localize("Connections.Detail.Group.Traffic"), [
                Row(Localize("Connections.Detail.Total"), row.TrafficText, "mono"),
                Row(Localize("Connections.Detail.Speed"), $"{row.UploadSpeedText}  {row.DownloadSpeedText}", "mono")
            ]),
            Group(Localize("Connections.Detail.Group.Meta"), [
                Row(Localize("Connections.Card.Duration"), FormatDuration(connection.Start, _now()), "mono"),
                Row(Localize("Connections.Detail.ConnectionId"), connection.Id, "mono")
            ])
        };
        return groups.Where(group => group.Rows.Count > 0).ToList();
    }

    private static ConnectionDetailGroupViewModel Group(string title, IEnumerable<ConnectionDetailRowViewModel> rows)
    {
        return new ConnectionDetailGroupViewModel(title, rows.Where(item => !string.IsNullOrWhiteSpace(item.Value)).ToList());
    }

    private static ConnectionDetailRowViewModel Row(string label, string value, string emphasis = "")
    {
        return new ConnectionDetailRowViewModel(label, value, emphasis);
    }

    private static string FormatEndpoint(string host, string port)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(port) ? host : $"{host}:{port}";
    }

    private static string FormatDuration(DateTimeOffset start, DateTimeOffset now)
    {
        if (start == default)
        {
            return string.Empty;
        }

        var duration = now - start;
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        return duration.ToString(@"hh\:mm\:ss");
    }

    // 单次遍历聚合数量和速率，避免每个属性访问器都扫描完整列表。
    private void RecomputeConnectionAggregates()
    {
        var directCount = 0;
        long uploadSpeed = 0;
        long downloadSpeed = 0;
        foreach (var connection in Connections)
        {
            if (connection.ProxyNode == "DIRECT")
            {
                directCount++;
            }

            uploadSpeed += connection.UploadSpeed;
            downloadSpeed += connection.DownloadSpeed;
        }

        _directConnectionCount = directCount;
        _totalUploadSpeed = _latestTrafficRate?.UploadSpeed ?? uploadSpeed;
        _totalDownloadSpeed = _latestTrafficRate?.DownloadSpeed ?? downloadSpeed;
    }

    // 缓存筛选行，让匹配的 Id 序列复用 VM 并保留滚动状态。
    private void RefreshFilteredConnections()
    {
        _filteredConnections = _filter.Apply(_state.Connections, _filterLevel, _searchKeyword);
        var now = _now();
        if (ConnectionRowsMatch(_filteredConnections))
        {
            for (var index = 0; index < _filteredConnections.Count; index++)
            {
                _visibleConnectionRows[index].Update(_filteredConnections[index], now);
            }

            return;
        }

        _visibleConnectionRows = _filteredConnections
            .Select(connection => new ConnectionRowViewModel(connection, now))
            .ToList();
        OnPropertyChanged(nameof(FilteredConnectionRows));
    }

    private bool ConnectionRowsMatch(IReadOnlyList<ConnectionInfo> connections)
    {
        if (_visibleConnectionRows.Count != connections.Count)
        {
            return false;
        }

        for (var index = 0; index < connections.Count; index++)
        {
            if (!string.Equals(_visibleConnectionRows[index].Id, connections[index].Id, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private void RaiseTotalSpeedChanged()
    {
        RecomputeConnectionAggregates();
        OnPropertyChanged(nameof(TotalUploadSpeedText));
        OnPropertyChanged(nameof(TotalDownloadSpeedText));
    }

    private void RaiseConnectionStateChanged()
    {
        RecomputeConnectionAggregates();
        RefreshFilteredConnections();
        OnPropertyChanged(nameof(Connections));
        OnPropertyChanged(nameof(FilteredConnections));
        OnPropertyChanged(nameof(TotalConnectionCount));
        OnPropertyChanged(nameof(FilteredConnectionCount));
        OnPropertyChanged(nameof(DirectConnectionCount));
        OnPropertyChanged(nameof(ProxyConnectionCount));
        OnPropertyChanged(nameof(TotalUploadSpeedText));
        OnPropertyChanged(nameof(TotalDownloadSpeedText));
        OnPropertyChanged(nameof(IsMonitoringPaused));
        OnPropertyChanged(nameof(MonitoringToggleTooltip));
        OnPropertyChanged(nameof(FilterLevel));
        OnPropertyChanged(nameof(IsAllConnectionsSelected));
        OnPropertyChanged(nameof(IsDirectConnectionsSelected));
        OnPropertyChanged(nameof(IsProxyConnectionsSelected));
        OnPropertyChanged(nameof(SearchKeyword));
        OnPropertyChanged(nameof(IsEmptyVisible));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(HasClosedAllConnections));
        OnPropertyChanged(nameof(ClosedConnectionIds));
    }

    // 仅在选择或语言变化时重建详情视图，不随列表刷新重建。
    private void RebuildSelectedConnectionDetail()
    {
        if (_selectedConnection is null)
        {
            _detailGroups = [];
            _detailLines = [];
            _detailText = string.Empty;
            _detailSummaryChain = string.Empty;
            _detailSummaryUpload = string.Empty;
            _detailSummaryDownload = string.Empty;
            _detailSummaryDuration = string.Empty;
            _detailSummaryRule = string.Empty;
            return;
        }

        var row = new ConnectionRowViewModel(_selectedConnection, _now());
        _detailGroups = BuildDetailGroups(_selectedConnection);
        _detailLines = BuildDetailLinesFromGroups(_detailGroups);
        _detailText = BuildDetailTextFromGroups(_detailGroups);
        _detailSummaryChain = string.IsNullOrWhiteSpace(row.ChainSummaryText)
            ? Localize("Connections.Stat.Direct")
            : row.ChainSummaryText;
        _detailSummaryUpload = row.UploadSpeedText;
        _detailSummaryDownload = row.DownloadSpeedText;
        _detailSummaryDuration = row.DurationText;
        _detailSummaryRule = $"{_selectedConnection.Rule} {_selectedConnection.RulePayload}".Trim();
    }

    private void RaiseDetailStateChanged()
    {
        RebuildSelectedConnectionDetail();
        OnPropertyChanged(nameof(SelectedConnection));
        OnPropertyChanged(nameof(IsDetailVisible));
        OnPropertyChanged(nameof(SelectedConnectionDetailLines));
        OnPropertyChanged(nameof(SelectedConnectionDetailText));
        OnPropertyChanged(nameof(SelectedConnectionDetailGroups));
        OnPropertyChanged(nameof(DetailSummaryChain));
        OnPropertyChanged(nameof(DetailSummaryUpload));
        OnPropertyChanged(nameof(DetailSummaryDownload));
        OnPropertyChanged(nameof(DetailSummaryDuration));
        OnPropertyChanged(nameof(DetailSummaryRule));
    }

    public void Dispose()
    {
        _detailCloseReset.Cancel();
        if (_localization is not null)
        {
            _localization.LanguageChanged -= OnLanguageChanged;
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs args)
    {
        RaiseConnectionStateChanged();
        RaiseDetailStateChanged();
    }

    private string Localize(string key) => _localization?.GetString(key) ?? key;
}
