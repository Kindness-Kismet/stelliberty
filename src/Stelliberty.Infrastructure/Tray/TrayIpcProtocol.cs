using System.Text.Json;

namespace Stelliberty.Infrastructure.Tray;

public sealed record TrayIpcRequest(string Id, string Method, JsonElement Parameters)
{
    public T DeserializeParameters<T>()
    {
        return Parameters.Deserialize<T>(TrayJson.Options)
            ?? throw new JsonException($"IPC parameters for {Method} are empty.");
    }
}

public sealed record TrayIpcResult(object? Value, string? ErrorCode, string? ErrorMessage)
{
    public bool IsSuccess => ErrorCode is null;

    public static TrayIpcResult Success(object? value) => new(value, null, null);

    public static TrayIpcResult Error(string code, string message) => new(null, code, message);
}

internal static class TrayJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };
}
