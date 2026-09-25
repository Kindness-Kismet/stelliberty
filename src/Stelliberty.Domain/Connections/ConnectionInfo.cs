namespace Stelliberty.Domain.Connections;

public sealed record ConnectionInfo(
    string Id,
    long Upload = 0,
    long Download = 0,
    long UploadSpeed = 0,
    long DownloadSpeed = 0,
    DateTimeOffset Start = default,
    ConnectionMetadata? Metadata = null,
    IReadOnlyList<string>? Chains = null,
    string Rule = "",
    string RulePayload = "")
{
    public ConnectionMetadata Metadata { get; init; } = Metadata ?? new ConnectionMetadata();

    public IReadOnlyList<string> Chains { get; init; } = Chains ?? [];

    private const string DirectProxy = "DIRECT";

    // 核心 chains 顺序：[实际出站, ..., 规则命中的策略]
    public string ProxyGroup => Chains.Count > 0 ? Chains[^1] : DirectProxy;

    public string ProxyNode => Chains.Count > 0 ? Chains[0] : DirectProxy;

    public bool IsDirect => ProxyNode == DirectProxy;

    public string LegacyProxyChain => string.Join(" → ", Chains.Reverse());
}
