namespace Stelliberty.Application.Runtime;

public interface IReadyCoreManager : ICoreManager
{
    Task EnsureReadyAsync(CancellationToken cancellationToken = default);
}

public sealed record CoreHostOperationResult(bool IsSuccess, string Message)
{
    public static CoreHostOperationResult Success(string message) => new(true, message);

    public static CoreHostOperationResult Failure(string message) => new(false, message);
}
