using Stelliberty.Application.Localization;
using Stelliberty.Domain.Subscriptions;
using Stelliberty.Presentation.Formatting;

namespace Stelliberty.Presentation.ViewModels;

public sealed class SubscriptionItemViewModel : ViewModelBase
{
    private bool _isCurrent;
    private bool _isUpdating;
    private readonly ILocalizationService? _localization;

    public SubscriptionItemViewModel(
        string id,
        string name,
        string sourceLocation,
        bool isLocalFile,
        string userAgent = "",
        int autoTestDelayIntervalMinutes = 0,
        SubscriptionAutoUpdateMode autoUpdateMode = SubscriptionAutoUpdateMode.Disabled,
        int autoUpdateIntervalMinutes = 0,
        SubscriptionUpdateProxyMode updateProxyMode = SubscriptionUpdateProxyMode.Direct,
        string ageSecretKey = "",
        bool isCurrent = false,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? lastUpdatedAt = null,
        int overrideCount = 0,
        int chainProxyCount = 0,
        long trafficUsed = 0,
        long trafficTotal = 0,
        long trafficExpire = 0,
        string? lastError = null,
        DateTimeOffset? lastErrorAt = null,
        SubscriptionSourceFormat sourceFormat = SubscriptionSourceFormat.StandardClash,
        ILocalizationService? localization = null,
        bool hasTrafficInfo = false)
    {
        Id = id;
        Name = name;
        SourceLocation = sourceLocation;
        IsLocalFile = isLocalFile;
        UserAgent = userAgent;
        AgeSecretKey = ageSecretKey;
        AutoTestDelayIntervalMinutes = autoTestDelayIntervalMinutes;
        AutoUpdateMode = autoUpdateMode;
        AutoUpdateIntervalMinutes = autoUpdateIntervalMinutes;
        UpdateProxyMode = updateProxyMode;
        _isCurrent = isCurrent;
        CreatedAt = createdAt;
        LastUpdatedAt = lastUpdatedAt;
        OverrideCount = overrideCount;
        ChainProxyCount = chainProxyCount;
        TrafficUsed = trafficUsed;
        TrafficTotal = trafficTotal;
        TrafficExpire = trafficExpire;
        HasTrafficInfo = hasTrafficInfo || trafficTotal > 0 || trafficUsed > 0;
        LastError = lastError;
        LastErrorAt = lastErrorAt;
        SourceFormat = sourceFormat;
        _localization = localization;
    }

    public string Id { get; }

    public string Name { get; }

    public string SourceLocation { get; }

    public bool IsLocalFile { get; }

    public string UserAgent { get; }

    public string AgeSecretKey { get; }

    public int AutoTestDelayIntervalMinutes { get; }

    public SubscriptionAutoUpdateMode AutoUpdateMode { get; }

    public int AutoUpdateIntervalMinutes { get; }

    public SubscriptionUpdateProxyMode UpdateProxyMode { get; }

    public bool IsCurrent
    {
        get => _isCurrent;
        private set
        {
            if (SetProperty(ref _isCurrent, value))
            {
                OnPropertyChanged(nameof(MenuOptions));
            }
        }
    }

    public DateTimeOffset? CreatedAt { get; }

    public DateTimeOffset? LastUpdatedAt { get; }

    public int OverrideCount { get; }

    public int ChainProxyCount { get; }

    public long TrafficUsed { get; }

    public long TrafficTotal { get; }

    public long TrafficExpire { get; }

    public string? LastError { get; }

    public DateTimeOffset? LastErrorAt { get; }

    public SubscriptionSourceFormat SourceFormat { get; }

    public bool HasError => !string.IsNullOrWhiteSpace(LastError);

    public bool IsAutoTestDelayEnabled => AutoTestDelayIntervalMinutes > 0;

    public string TypeText => IsLocalFile ? Localize("Subscriptions.Type.Local") : Localize("Subscriptions.Type.Remote");

    public string TypePillTag => IsLocalFile ? "local" : "remote";

    public string SourceFormatText => SourceFormat == SubscriptionSourceFormat.StandardClash
        ? Localize("Subscriptions.SourceFormat.Standard")
        : Localize("Subscriptions.SourceFormat.NonStandard");

    public string SourceFormatPillTag => SourceFormat == SubscriptionSourceFormat.StandardClash ? "success" : "warning";

    public string IconType => HasError ? "WarningFill" : IsLocalFile ? "FileLine" : "CloudLine";

    public string IconTag => HasError ? "error" : TypePillTag;

    public string AutoUpdateText => AutoUpdateMode switch
    {
        SubscriptionAutoUpdateMode.Startup => Localize("Subscriptions.AutoUpdate.Startup"),
        SubscriptionAutoUpdateMode.Interval => AutoUpdateIntervalMinutes > 0
            ? string.Format(Localize("Subscriptions.AutoUpdate.IntervalMinutes"), AutoUpdateIntervalMinutes)
            : Localize("Subscriptions.AutoUpdate.Interval"),
        _ => Localize("Subscriptions.AutoUpdate.Manual")
    };

    public bool IsAutoUpdatePillVisible => !IsLocalFile;

    public string AutoTestDelayText => IsAutoTestDelayEnabled
        ? string.Format(Localize("Subscriptions.AutoDelay.IntervalMinutes"), AutoTestDelayIntervalMinutes)
        : Localize("Subscriptions.AutoDelay.Disabled");

    public string UpdateProxyText => UpdateProxyMode switch
    {
        SubscriptionUpdateProxyMode.SystemProxy => Localize("Subscriptions.Proxy.System"),
        SubscriptionUpdateProxyMode.Core => Localize("Subscriptions.Proxy.Core"),
        _ => Localize("Subscriptions.Proxy.Direct")
    };

    public string LastUpdatedText => LastUpdatedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? Localize("Common.NotUpdated");

    public string OverrideSummaryText => OverrideCount > 0
        ? string.Format(Localize("Subscriptions.Override.Count"), OverrideCount)
        : Localize("Subscriptions.Override.None");

    public string ChainProxySummaryText => ChainProxyCount > 0
        ? string.Format(Localize("Subscriptions.ChainProxy.Count"), ChainProxyCount)
        : Localize("Subscriptions.ChainProxy.None");

    public string TrafficText => HasTrafficInfo
        ? $"{ByteSize.Format(TrafficUsed)} / {(HasTrafficTotal ? ByteSize.Format(TrafficTotal) : Localize("Subscriptions.Traffic.TotalUnknown"))}"
        : Localize("Subscriptions.Traffic.Unavailable");

    public bool HasTrafficInfo { get; }

    public bool HasTrafficTotal => TrafficTotal > 0;

    public string TrafficAutomationId => $"Subscriptions.Row.{Id}.TrafficText";

    public double TrafficUsageRatio => TrafficTotal > 0 ? Math.Clamp((double)TrafficUsed / TrafficTotal, 0, 1) : 0;

    public string ExpireText => TrafficExpire > 0
        ? DateTimeOffset.FromUnixTimeSeconds(TrafficExpire).ToLocalTime().ToString("yyyy-MM-dd")
        : Localize("Common.Unknown");

    public bool IsExpireInfoVisible => !IsLocalFile || TrafficExpire > 0;

    public int LastUpdatedInfoColumnSpan => IsExpireInfoVisible ? 1 : 2;

    public string RowAutomationId => $"Subscriptions.Row.{Id}";

    public string SelectAutomationId => $"Subscriptions.Row.{Id}.SelectButton";

    public string NameAutomationId => $"Subscriptions.Row.{Id}.NameText";

    public string UpdateAutomationId => $"Subscriptions.Row.{Id}.UpdateButton";

    public bool IsUpdateVisible => !IsLocalFile;

    public bool IsUpdating
    {
        get => _isUpdating;
        private set
        {
            if (SetProperty(ref _isUpdating, value))
            {
                OnPropertyChanged(nameof(IsUpdateIconVisible));
                OnPropertyChanged(nameof(IsUpdateButtonEnabled));
            }
        }
    }

    public bool IsUpdateIconVisible => !IsUpdating;

    public bool IsUpdateButtonEnabled => !IsUpdating;

    public string MenuAutomationId => $"Subscriptions.Row.{Id}.MenuButton";

    public string IconAutomationId => $"Subscriptions.Row.{Id}.Icon";

    public void SetCurrent(bool isCurrent)
    {
        IsCurrent = isCurrent;
    }

    public void SetUpdating(bool isUpdating)
    {
        IsUpdating = isUpdating;
    }

    public SubscriptionItemViewModel WithConfiguration(
        string name,
        string sourceLocation,
        string userAgent,
        string ageSecretKey,
        int autoTestDelayIntervalMinutes,
        SubscriptionAutoUpdateMode autoUpdateMode,
        int autoUpdateIntervalMinutes,
        SubscriptionUpdateProxyMode updateProxyMode)
    {
        return new SubscriptionItemViewModel(
            Id,
            name,
            sourceLocation,
            IsLocalFile,
            userAgent,
            autoTestDelayIntervalMinutes,
            autoUpdateMode,
            autoUpdateIntervalMinutes,
            updateProxyMode,
            ageSecretKey,
            IsCurrent,
            CreatedAt,
            LastUpdatedAt,
            OverrideCount,
            ChainProxyCount,
            TrafficUsed,
            TrafficTotal,
            TrafficExpire,
            LastError,
            LastErrorAt,
            SourceFormat,
            _localization,
            HasTrafficInfo);
    }

    public IReadOnlyList<SubscriptionRowMenuSelection> MenuOptions
    {
        get
        {
            var options = new List<SubscriptionRowMenuSelection>
            {
                new(Id, SubscriptionRowMenuAction.Edit, Localize("Subscriptions.Menu.Edit")),
                new(Id, SubscriptionRowMenuAction.ChainProxy, Localize("Subscriptions.Menu.ChainProxy")),
                new(Id, SubscriptionRowMenuAction.EditFile, Localize("Subscriptions.Menu.EditFile")),
                new(Id, SubscriptionRowMenuAction.OpenExternalEditor, Localize("Subscriptions.Menu.ExternalEditor"))
            };

            if (IsCurrent)
            {
                options.Add(new(Id, SubscriptionRowMenuAction.ViewRuntimeConfig, Localize("Subscriptions.Menu.RuntimeConfig")));
            }

            options.Add(new(Id, SubscriptionRowMenuAction.OverrideSelector, Localize("Subscriptions.Menu.Overrides")));
            options.Add(new(Id, SubscriptionRowMenuAction.ProviderSelector, Localize("Subscriptions.Menu.Providers")));
            if (!IsLocalFile)
            {
                options.Add(new(Id, SubscriptionRowMenuAction.CopyLink, Localize("Subscriptions.Menu.CopyLink")));
                options.Add(new(Id, SubscriptionRowMenuAction.QrCode, Localize("Subscriptions.Menu.QrCode")));
            }

            options.Add(new(Id, SubscriptionRowMenuAction.Delete, Localize("Common.Delete")));
            return options;
        }
    }

    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(TypeText));
        OnPropertyChanged(nameof(SourceFormatText));
        OnPropertyChanged(nameof(AutoUpdateText));
        OnPropertyChanged(nameof(IsAutoUpdatePillVisible));
        OnPropertyChanged(nameof(AutoTestDelayText));
        OnPropertyChanged(nameof(UpdateProxyText));
        OnPropertyChanged(nameof(LastUpdatedText));
        OnPropertyChanged(nameof(OverrideSummaryText));
        OnPropertyChanged(nameof(ChainProxySummaryText));
        OnPropertyChanged(nameof(TrafficText));
        OnPropertyChanged(nameof(ExpireText));
        OnPropertyChanged(nameof(MenuOptions));
    }

    private string Localize(string key) => _localization?.GetString(key) ?? key;
}
