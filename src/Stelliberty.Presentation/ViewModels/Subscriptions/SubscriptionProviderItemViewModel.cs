using Stelliberty.Application.Localization;
using Stelliberty.Domain.Subscriptions;
using Stelliberty.Presentation.Formatting;

namespace Stelliberty.Presentation.ViewModels;

public sealed record SubscriptionProviderItemViewModel(
    string Name,
    string DisplayName,
    string Type,
    string VehicleType,
    int Count,
    string UpdatedAt,
    bool HasRuntimeState = false,
    bool IsSyncing = false,
    bool IsSynced = false,
    bool IsUploaded = false,
    ILocalizationService? Localization = null,
    SubscriptionTrafficInfo? TrafficInfo = null,
    bool CanManage = true,
    bool IsCached = false)
{
    public bool IsRemote => string.Equals(VehicleType, "HTTP", StringComparison.OrdinalIgnoreCase);

    public bool IsFile => string.Equals(VehicleType, "File", StringComparison.OrdinalIgnoreCase);

    public bool CanSync => IsRemote && CanManage;

    public bool CanUpload => IsFile && CanManage;

    public bool IsSyncEnabled => CanSync && !IsSyncing;

    public bool IsTrafficVisible => IsRemote && !IsRule;

    public bool HasTrafficTotal => TrafficInfo is { Total: > 0 };

    public double TrafficUsageRatio => TrafficInfo is { Total: > 0 } info
        ? Math.Clamp((double)info.Used / info.Total, 0, 1)
        : 0;

    // 额度使用达到九成时提示余量不足。
    public bool IsTrafficWarning => TrafficUsageRatio >= 0.9;

    public string TrafficText => TrafficInfo is { } info
        ? $"{ByteSize.Format(info.Used)} / {(HasTrafficTotal ? ByteSize.Format(info.Total) : Localize("Subscriptions.Traffic.TotalUnknown"))}"
        : Localize("Subscriptions.Traffic.Unavailable");

    public string ExpireText => TrafficInfo is { Expire: > 0 } info
        ? DateTimeOffset.FromUnixTimeSeconds(info.Expire).ToLocalTime().ToString("yyyy-MM-dd")
        : Localize("Common.Unknown");

    // 七天内到期提示续费，已到期仍保留提醒。
    public bool IsExpireWarning => TrafficInfo is { Expire: > 0 } info
        && DateTimeOffset.FromUnixTimeSeconds(info.Expire) <= DateTimeOffset.UtcNow.AddDays(7);

    public string ExpireStatusText => TrafficInfo is { Expire: > 0 } info
        && DateTimeOffset.FromUnixTimeSeconds(info.Expire) <= DateTimeOffset.UtcNow
            ? Localize("Subscriptions.Traffic.Expired")
            : Localize("Subscriptions.Traffic.ExpiringSoon");

    public string TrafficAutomationId => $"Subscriptions.ProviderSelector.{Type}.{Name}.TrafficText";

    public string TrafficBarAutomationId => $"Subscriptions.ProviderSelector.{Type}.{Name}.TrafficBar";

    public string ExpireAutomationId => $"Subscriptions.ProviderSelector.{Type}.{Name}.ExpireText";

    public string StatAutomationId => $"Subscriptions.ProviderSelector.{Type}.{Name}.StatText";

    public string ExpireStatusAutomationId => $"Subscriptions.ProviderSelector.{Type}.{Name}.ExpireStatusText";

    public string SyncAutomationId => $"Subscriptions.ProviderSelector.{Type}.{Name}.SyncButton";

    public string UploadAutomationId => $"Subscriptions.ProviderSelector.{Type}.{Name}.UploadButton";

    public string NameAutomationId => $"Subscriptions.ProviderSelector.{Type}.{Name}.NameText";

    public string VehiclePillTag => IsRemote ? "info" : "warning";

    public string VehicleIconType => IsFile ? "FileLine" : "CloudLine";

    // 文件 Provider 使用本地灰色徽标；HTTP Provider 使用默认强调色。
    public string VehicleBadgeTag => IsFile ? "local" : "remote";

    public string VehicleText => Localize(IsFile ? "Subscriptions.Type.Local" : "Subscriptions.Type.Remote");

    // 缺失运行时状态表示订阅未激活，不是零 providers。
    public string StatText => HasRuntimeState
        ? $"{CountText} · {(IsCached ? string.Format(Localize("Subscriptions.Traffic.Cached"), UpdatedAt) : UpdatedAt)}"
        : UpdatedAt;

    public string CountText => string.Format(
        Localize(IsRule ? "Subscriptions.Provider.RuleCount" : "Subscriptions.Provider.ProxyCount"),
        Count);

    private bool IsRule => string.Equals(Type, "rule", StringComparison.OrdinalIgnoreCase);

    private string Localize(string key) => Localization?.GetString(key) ?? key;
}
