using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Stelliberty.Application.Platform;

namespace Stelliberty.Desktop.Services;

// 直连服务命令管道，帧格式与 Rust native/service ipc.rs 一致：4 字节小端长度 + UTF-8 JSON。
internal static class ServiceCommandPipeClient
{
    // 必须与 Rust native/service/src/ipc.rs MAX_RESPONSE_BYTES 一致
    private const int MaxResponseBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static async Task<ServiceStatusPayload> RequestStatusAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        await using var stream = await ConnectAsync(cts.Token).ConfigureAwait(false);
        // 服务每个连接只处理一条命令，写完即读。
        await WriteFrameAsync(stream, """{"type":"Status"}""", cts.Token).ConfigureAwait(false);
        var payload = await ReadFrameAsync(stream, cts.Token).ConfigureAwait(false);
        var envelope = JsonSerializer.Deserialize<ServiceResponseEnvelope>(payload, SerializerOptions)
            ?? throw new InvalidDataException("Service status response is empty.");

        if (envelope.Type != "Status")
        {
            throw new InvalidDataException($"Unexpected service response: {envelope.Type}");
        }

        return envelope.Data.Deserialize<ServiceStatusPayload>(SerializerOptions)
            ?? throw new InvalidDataException("Service status payload is empty.");
    }

    private static async Task<Stream> ConnectAsync(CancellationToken cancellationToken)
    {
        var endpoint = AppRuntimeNames.ServiceCommandEndpoint;
        if (OperatingSystem.IsWindows())
        {
            // NamedPipeClientStream 只接受管道名，ServiceCommandEndpoint 已带 \\.\pipe\ 前缀需剥离
            var pipeName = endpoint[@"\\.\pipe\".Length..];
            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
                return pipe;
            }
            catch
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(endpoint), cancellationToken).ConfigureAwait(false);
        return new NetworkStream(socket, ownsSocket: true);
    }

    private static async Task WriteFrameAsync(Stream stream, string json, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var header = BitConverter.GetBytes((uint)payload.Length);
        if (!BitConverter.IsLittleEndian)
        {
            Array.Reverse(header);
        }

        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        if (!BitConverter.IsLittleEndian)
        {
            Array.Reverse(header);
        }

        var length = BitConverter.ToUInt32(header);
        if (length > MaxResponseBytes)
        {
            throw new InvalidDataException($"Service response frame is too large: {length}");
        }

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        return payload;
    }

    private sealed record ServiceResponseEnvelope(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("data")] JsonElement Data);
}
