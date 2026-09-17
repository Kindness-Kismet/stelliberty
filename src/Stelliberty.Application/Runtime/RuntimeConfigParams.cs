using Stelliberty.Application.Settings;

namespace Stelliberty.Application.Runtime;

public sealed record RuntimeConfigParams
{
    public static RuntimeConfigParams Default { get; } = new();

    public static RuntimeConfigParams FromSettings(AppSettings settings)
    {
        return Default with
        {
            MixedPort = NormalizeRequiredPort(settings.MixedPort, Default.MixedPort),
            SocksPort = NormalizeOptionalPort(settings.SocksPort),
            HttpPort = NormalizeOptionalPort(settings.HttpPort),
            IsIpv6Enabled = settings.IsIpv6Enabled,
            IsAllowLanEnabled = settings.IsAllowLanEnabled,
            IsTcpConcurrentEnabled = settings.IsTcpConcurrentEnabled,
            IsUnifiedDelayEnabled = settings.IsUnifiedDelayEnabled,
            OutboundMode = NormalizeOutboundMode(settings.OutboundMode),
            IsTunEnabled = settings.IsTunEnabled,
            TunStack = NormalizeTunStack(settings.TunStack),
            TunDevice = TrimOrDefault(settings.TunDevice, Default.TunDevice),
            IsTunAutoRouteEnabled = settings.IsTunAutoRouteEnabled,
            IsTunAutoRedirectEnabled = settings.IsTunAutoRedirectEnabled,
            IsTunAutoDetectInterfaceEnabled = settings.IsTunAutoDetectInterfaceEnabled,
            IsTunStrictRouteEnabled = settings.IsTunStrictRouteEnabled,
            TunDnsHijacks = TrimListOrDefault(settings.TunDnsHijack, Default.TunDnsHijacks),
            TunRouteExcludeAddresses = TrimList(settings.TunRouteExcludeAddresses),
            IsTunIcmpForwardingDisabled = settings.IsTunIcmpForwardingDisabled,
            TunMtu = settings.TunMtu ?? Default.TunMtu,
            GeodataLoader = settings.GeoDataLoader,
            FindProcessMode = settings.FindProcessMode,
            ClashCoreLogLevel = settings.CoreLogLevel,
            ExternalController = BuildExternalController(settings),
            ExternalControllerSecret = BuildExternalControllerSecret(settings),
            IsKeepAliveEnabled = settings.IsTcpKeepAliveEnabled,
            KeepAliveInterval = settings.TcpKeepAliveInterval,
            IsDnsOverrideEnabled = settings.IsDnsOverrideEnabled,
            DnsListen = settings.DnsListen.Trim(),
            IsDnsIpv6Enabled = settings.IsDnsIpv6Enabled,
            FakeIpFilterMode = settings.FakeIpFilterMode.Trim(),
            DnsOverrideContent = BuildDnsOverrideContent(settings),
            LanAuthentication = BuildLanAuthentication(settings),
            LanAllowedIps = TrimList(settings.LanAllowedIps),
            LanDisallowedIps = TrimList(settings.LanDisallowedIps),
            SkipAuthPrefixes = TrimList(settings.SkipAuthPrefixes)
        };
    }

    public int MixedPort { get; init; } = AppSettings.DefaultMixedPort;

    public int SocksPort { get; init; } = 0;

    public int HttpPort { get; init; } = 0;

    public bool IsIpv6Enabled { get; init; }

    public bool IsAllowLanEnabled { get; init; }

    public bool IsTcpConcurrentEnabled { get; init; }

    public bool IsUnifiedDelayEnabled { get; init; }

    public string OutboundMode { get; init; } = "Rule";

    public bool IsTunEnabled { get; init; }

    public string TunStack { get; init; } = "mixed";

    public string TunDevice { get; init; } = AppSettings.DefaultTunDevice;

    public bool IsTunAutoRouteEnabled { get; init; } = true;

    public bool IsTunAutoRedirectEnabled { get; init; } = true;

    public bool IsTunAutoDetectInterfaceEnabled { get; init; } = true;

    public IReadOnlyList<string> TunDnsHijacks { get; init; } = ["any:53"];

    public bool IsTunStrictRouteEnabled { get; init; } = true;

    public IReadOnlyList<string> TunRouteExcludeAddresses { get; init; } = [];

    public bool IsTunIcmpForwardingDisabled { get; init; }

    public int TunMtu { get; init; } = AppSettings.DefaultTunMtu;

    public string GeodataLoader { get; init; } = "standard";

    public string FindProcessMode { get; init; } = "off";

    public string ClashCoreLogLevel { get; init; } = "silent";

    public string? ExternalController { get; init; }

    public string? ExternalControllerSecret { get; init; }

    public bool IsKeepAliveEnabled { get; init; }

    public int? KeepAliveInterval { get; init; }

    public bool IsDnsOverrideEnabled { get; init; }

    public string DnsListen { get; init; } = ":53";

    public bool IsDnsIpv6Enabled { get; init; } = true;

    public string FakeIpFilterMode { get; init; } = DnsDefaults.FakeIpFilterMode;

    public IReadOnlyList<string> ProxyServerNameServers { get; init; } = DnsDefaults.ProxyServerNameServers;

    public string? DnsOverrideContent { get; init; }

    public IReadOnlyList<string> LanAuthentication { get; init; } = [];

    public IReadOnlyList<string> LanAllowedIps { get; init; } = [];

    public IReadOnlyList<string> LanDisallowedIps { get; init; } = [];

    public IReadOnlyList<string> SkipAuthPrefixes { get; init; } = [];

    // 端口遵循 TCP/UDP 范围；可选端口用 0 表示关闭。
    private static int NormalizeRequiredPort(int value, int fallback)
    {
        return value is >= 1 and <= 65535 ? value : fallback;
    }

    private static int NormalizeOptionalPort(int? value)
    {
        return value is >= 1 and <= 65535 ? value.Value : 0;
    }

    private static string NormalizeTunStack(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? Default.TunStack : value.Trim().ToLowerInvariant();
    }

    // 设置可能被手动编辑，白名单外的值回退为 Rule。
    private static string NormalizeOutboundMode(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "global" => "Global",
            "direct" => "Direct",
            _ => "Rule"
        };
    }

    private static string? BuildExternalController(AppSettings settings)
    {
        return settings.IsExternalControllerEnabled && !string.IsNullOrWhiteSpace(settings.ExternalControllerAddress)
            ? settings.ExternalControllerAddress.Trim()
            : null;
    }

    private static string? BuildExternalControllerSecret(AppSettings settings)
    {
        var secret = settings.ExternalControllerSecret.Trim();
        return BuildExternalController(settings) is null || string.IsNullOrWhiteSpace(secret) ? null : secret;
    }

    private static IReadOnlyList<string> BuildLanAuthentication(AppSettings settings)
    {
        var userName = settings.LanAuthenticationUserName.Trim();
        var password = settings.LanAuthenticationPassword.Trim();
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            return [];
        }

        return [$"{userName}:{password}"];
    }

    private static string? BuildDnsOverrideContent(AppSettings settings)
    {
        if (!settings.IsDnsOverrideEnabled)
        {
            return null;
        }

        var dns = new List<string>
        {
            "dns:",
            $"  enable: {ToYamlBool(settings.IsDnsEnabled)}",
            $"  listen: {YamlString(settings.DnsListen.Trim())}",
            $"  ipv6: {ToYamlBool(settings.IsDnsIpv6Enabled)}",
            $"  enhanced-mode: {settings.DnsEnhancedMode.Trim()}",
            $"  fake-ip-range: {settings.FakeIpRange.Trim()}",
            $"  respect-rules: {ToYamlBool(settings.IsDnsRespectRulesEnabled)}",
            $"  use-hosts: {ToYamlBool(settings.IsDnsUseHostsEnabled)}",
            $"  use-system-hosts: {ToYamlBool(settings.IsDnsUseSystemHostsEnabled)}",
            $"  prefer-h3: {ToYamlBool(settings.IsDnsPreferH3Enabled)}",
            $"  direct-nameserver-follow-policy: {ToYamlBool(settings.IsDirectNameServerFollowPolicyEnabled)}"
        };
        AddScalar(dns, "fake-ip-filter-mode", settings.FakeIpFilterMode);
        AddSequence(dns, "nameserver", settings.NameServers);
        AddSequence(dns, "fallback", settings.FallbackNameServers);
        AddSequence(dns, "proxy-server-nameserver", EffectiveProxyServerNameServers(settings));
        AddSequence(dns, "default-nameserver", settings.DefaultNameServers);
        AddSequence(dns, "fake-ip-filter", settings.FakeIpFilters);
        AddSequence(dns, "direct-nameserver", settings.DirectNameServers);
        AddMap(dns, "nameserver-policy", settings.NameServerPolicy);
        AddFallbackFilter(dns, settings);
        AddHosts(dns, settings.Hosts);
        return string.Join('\n', dns);
    }

    private static void AddScalar(List<string> lines, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add($"  {key}: {YamlString(value.Trim())}");
        }
    }

    private static IReadOnlyList<string> EffectiveProxyServerNameServers(AppSettings settings)
    {
        if (!settings.IsDnsRespectRulesEnabled || settings.ProxyServerNameServers.Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            return TrimList(settings.ProxyServerNameServers);
        }

        return Default.ProxyServerNameServers;
    }

    private static void AddSequence(List<string> lines, string key, IReadOnlyList<string> values)
    {
        var entries = TrimList(values);
        if (entries.Count == 0)
        {
            return;
        }

        lines.Add($"  {key}:");
        foreach (var value in entries)
        {
            lines.Add($"    - {YamlString(value)}");
        }
    }

    private static void AddMap(List<string> lines, string key, IReadOnlyList<string> values)
    {
        var entries = SplitMapEntries(values);
        if (entries.Count == 0)
        {
            return;
        }

        lines.Add($"  {key}:");
        foreach (var (mapKey, mapValue) in entries)
        {
            var mapValues = SplitMapValue(mapValue);
            if (mapValues.Count == 1)
            {
                lines.Add($"    {mapKey}: {YamlString(mapValues[0])}");
                continue;
            }

            lines.Add($"    {mapKey}:");
            foreach (var value in mapValues)
            {
                lines.Add($"      - {YamlString(value)}");
            }
        }
    }

    private static void AddFallbackFilter(List<string> lines, AppSettings settings)
    {
        if (!settings.IsFallbackFilterGeoIpEnabled
            && string.IsNullOrWhiteSpace(settings.FallbackFilterGeoIpCode)
            && settings.FallbackFilterIpCidrs.Count == 0
            && settings.FallbackFilterDomains.Count == 0)
        {
            return;
        }

        lines.Add("  fallback-filter:");
        lines.Add($"    geoip: {ToYamlBool(settings.IsFallbackFilterGeoIpEnabled)}");
        if (!string.IsNullOrWhiteSpace(settings.FallbackFilterGeoIpCode))
        {
            lines.Add($"    geoip-code: {YamlString(settings.FallbackFilterGeoIpCode.Trim())}");
        }

        AddNestedSequence(lines, "ipcidr", settings.FallbackFilterIpCidrs);
        AddNestedSequence(lines, "domain", settings.FallbackFilterDomains);
    }

    private static void AddNestedSequence(List<string> lines, string key, IReadOnlyList<string> values)
    {
        var entries = TrimList(values);
        if (entries.Count == 0)
        {
            return;
        }

        lines.Add($"    {key}:");
        foreach (var value in entries)
        {
            lines.Add($"      - {YamlString(value)}");
        }
    }

    private static void AddHosts(List<string> lines, IReadOnlyList<string> hosts)
    {
        var entries = SplitMapEntries(hosts);
        if (entries.Count == 0)
        {
            return;
        }

        lines.Add("hosts:");
        foreach (var (key, value) in entries)
        {
            lines.Add($"  {key}: {YamlString(value)}");
        }
    }

    private static IReadOnlyList<(string Key, string Value)> SplitMapEntries(IReadOnlyList<string> values)
    {
        var entries = new List<(string Key, string Value)>();
        foreach (var value in values)
        {
            var separatorIndex = value.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = value[..separatorIndex].Trim();
            var mapValue = value[(separatorIndex + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(mapValue))
            {
                entries.Add((key, mapValue));
            }
        }

        return entries;
    }

    private static IReadOnlyList<string> SplitMapValue(string value)
    {
        return value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static string TrimOrDefault(string value, string fallback)
    {
        var trimmed = value.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed;
    }

    private static IReadOnlyList<string> TrimList(IReadOnlyList<string> values)
    {
        return values
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
    }

    private static IReadOnlyList<string> TrimListOrDefault(IReadOnlyList<string> values, IReadOnlyList<string> fallback)
    {
        var trimmed = TrimList(values);
        return trimmed.Count == 0 ? fallback : trimmed;
    }

    private static string YamlString(string value)
    {
        return $"'{value.Replace("'", "''")}'";
    }

    private static string ToYamlBool(bool value)
    {
        return value ? "true" : "false";
    }
}
