namespace Stelliberty.Application.Proxies;

public interface IProxyDelayResultSink
{
    Task<string> CaptureScopeAsync(CancellationToken cancellationToken = default);

    Task PublishAsync(ProxyDelayPublication publication, CancellationToken cancellationToken = default);
}

public sealed record ProxyDelayPublication(string Scope, string ProxyName, int Delay, DateTimeOffset CompletedAt);
