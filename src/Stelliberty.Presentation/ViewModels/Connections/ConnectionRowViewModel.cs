using Stelliberty.Application.Connections;
using Stelliberty.Domain.Connections;
using Stelliberty.Presentation.Formatting;

namespace Stelliberty.Presentation.ViewModels;

public sealed class ConnectionRowViewModel : ViewModelBase
{
    private ConnectionInfo _connection;
    private DateTimeOffset? _now;

    public ConnectionRowViewModel(ConnectionInfo connection, DateTimeOffset? now = null)
    {
        _connection = connection;
        _now = now;
    }

    // 同 Id 刷新原地更新，保留行实例和虚拟化状态。
    public void Update(ConnectionInfo connection, DateTimeOffset? now)
    {
        _connection = connection;
        _now = now;
        OnPropertyChanged("");
    }

    public ConnectionInfo Connection => _connection;

    public string Id => _connection.Id;

    public string RowAutomationId => $"Connections.Row.{Id}";

    public string CloseAutomationId => $"Connections.Row.{Id}.CloseButton";

    public string NetworkAutomationId => $"Connections.Row.{Id}.NetworkText";

    public string DurationAutomationId => $"Connections.Row.{Id}.DurationText";

    public string ChainAutomationId => $"Connections.Row.{Id}.ChainText";

    public string Description => _connection.Metadata.Description;

    public string DisplayAddress
    {
        get
        {
            var host = _connection.Metadata.DisplayHost;
            var port = _connection.Metadata.DestinationPort;
            if (string.IsNullOrWhiteSpace(host))
            {
                return port;
            }
            return string.IsNullOrWhiteSpace(port) ? host : $"{host}:{port}";
        }
    }

    public string NetworkLabel => _connection.Metadata.Network.ToUpperInvariant();

    public string NetworkPillTag => _connection.Metadata.Network?.ToLowerInvariant() switch
    {
        "tcp" => "tcp",
        "udp" => "udp",
        _ => "neutral"
    };

    public string DurationText => FormatDuration(_connection.Start, _now ?? DateTimeOffset.Now);

    public string Rule => _connection.Rule;

    public string Process => _connection.Metadata.Process;

    public string UploadSpeedText => $"↑ {ByteSize.Format(_connection.UploadSpeed)}/s";

    public string DownloadSpeedText => $"↓ {ByteSize.Format(_connection.DownloadSpeed)}/s";

    public string TrafficText => $"{ByteSize.Format(_connection.Upload)} / {ByteSize.Format(_connection.Download)}";

    public string ChainSummaryText => string.Join(" / ", _connection.Chains);

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
}
