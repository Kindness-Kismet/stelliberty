namespace Stelliberty.Application.Platform;

public interface ISystemProxyController
{
    event EventHandler<SystemProxyStatus>? StatusChanged;

    Task<SystemProxyStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<SystemProxyApplyResult> SetEnabledAsync(
        bool isEnabled,
        SystemProxyApplicationRequest? request,
        CancellationToken cancellationToken = default);
}

public sealed record SystemProxyStatus(bool IsEnabled, bool IsOwned);

public sealed record SystemProxyApplyResult(
    bool IsSuccess,
    string Message,
    SystemProxyStatus Status);
