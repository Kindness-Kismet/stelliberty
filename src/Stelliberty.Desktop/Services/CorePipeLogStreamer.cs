using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Stelliberty.Application.CoreLogs;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Domain.CoreLogs;

namespace Stelliberty.Desktop.Services;

internal sealed class CorePipeLogStreamer : IDisposable
{
    private const int MaxFrameBytes = 1024 * 1024;
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(2);

    private readonly string _pipeName;
    private readonly CoreLogParser _parser = new();
    private readonly object _gate = new();
    private CancellationTokenSource? _streamCancellation;
    private Task? _streamTask;
    private long _streamGeneration;
    private bool _isDisposed;

    public CorePipeLogStreamer(string corePipe)
    {
        _pipeName = NormalizeEndpoint(corePipe);
    }

    public event EventHandler<CoreLogMessage>? MessageReceived;

    public void Start()
    {
        long generation;
        lock (_gate)
        {
            if (_isDisposed || _streamCancellation is not null)
            {
                return;
            }

            generation = StartLocked();
        }

        AppLogger.Info($"Service-mode core log stream started: generation={generation}");
    }

    public void Restart()
    {
        long generation;
        var replacedActiveStream = false;
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            replacedActiveStream = StopLocked() is not null;
            generation = StartLocked();
        }

        AppLogger.Info($"Service-mode core log stream restarted: generation={generation} replacedActive={replacedActiveStream.ToString().ToLowerInvariant()}");
    }

    public void Stop()
    {
        long? generation;
        lock (_gate)
        {
            generation = StopLocked();
        }

        if (generation is not null)
        {
            AppLogger.Info($"Service-mode core log stream stopped: generation={generation}");
        }
    }

    public void Dispose()
    {
        long? generation;
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            generation = StopLocked();
        }

        MessageReceived = null;
        if (generation is not null)
        {
            AppLogger.Info($"Service-mode core log stream disposed: generation={generation}");
        }
    }

    private long StartLocked()
    {
        var generation = ++_streamGeneration;
        var cancellation = new CancellationTokenSource();
        _streamCancellation = cancellation;
        _streamTask = Task.Run(() => RunAsync(generation, cancellation.Token));
        return generation;
    }

    private long? StopLocked()
    {
        var cancellation = _streamCancellation;
        var task = _streamTask;
        _streamCancellation = null;
        _streamTask = null;
        if (cancellation is null)
        {
            return null;
        }

        cancellation.Cancel();
        DisposeCancellationAfterTask(cancellation, task);
        return _streamGeneration;
    }

    private static void DisposeCancellationAfterTask(CancellationTokenSource cancellation, Task? task)
    {
        if (task is null || task.IsCompleted)
        {
            cancellation.Dispose();
            return;
        }

        task.ContinueWith(
            _ => cancellation.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task RunAsync(long generation, CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            attempt++;
            try
            {
                var ended = await StreamOnceAsync(generation, attempt, cancellationToken).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (ShouldLogAttempt(attempt))
                {
                    AppLogger.Warning(
                        $"Service-mode core log stream disconnected: generation={generation} attempt={attempt} reason={ended.Reason} payloads={ended.PayloadCount} messages={ended.MessageCount} elapsed={ended.Elapsed.TotalMilliseconds:0}ms retry={ReconnectDelay.TotalSeconds:0}s");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                if (ShouldLogAttempt(attempt))
                {
                    AppLogger.Warning(
                        $"Service-mode core log stream interrupted: generation={generation} attempt={attempt} error={exception.GetType().Name} message={exception.Message} retry={ReconnectDelay.TotalSeconds:0}s");
                }
            }

            await Task.Delay(ReconnectDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<StreamEnd> StreamOnceAsync(long generation, int attempt, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        await using var stream = await ConnectStreamAsync(_pipeName, cancellationToken).ConfigureAwait(false);
        await WriteHandshakeAsync(stream, cancellationToken).ConfigureAwait(false);
        await ReadHandshakeAsync(stream, cancellationToken).ConfigureAwait(false);
        if (ShouldLogAttempt(attempt))
        {
            AppLogger.Info($"Service-mode core log stream connected: generation={generation} attempt={attempt}");
        }

        List<byte>? fragmentedPayload = null;
        var payloadCount = 0;
        var messageCount = 0;
        var hasLoggedFirstPayload = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            var frame = await ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);
            if (frame is null)
            {
                return new StreamEnd("end-of-stream", payloadCount, messageCount, Stopwatch.GetElapsedTime(startedAt));
            }

            if (frame.Value.Opcode == 0x8)
            {
                return new StreamEnd("close-frame", payloadCount, messageCount, Stopwatch.GetElapsedTime(startedAt));
            }

            if (frame.Value.Opcode == 0x9)
            {
                await WriteClientFrameAsync(stream, 0xA, frame.Value.Payload, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (frame.Value.Opcode is 0x1 or 0x2)
            {
                if (fragmentedPayload is not null)
                {
                    throw new IOException("Log WebSocket started a new message before the fragmented message ended");
                }

                if (frame.Value.IsFinal)
                {
                    payloadCount++;
                    var parsed = Publish(Encoding.UTF8.GetString(frame.Value.Payload));
                    messageCount += parsed;
                    LogFirstPayload(generation, attempt, frame.Value.Payload.Length, parsed, fragmented: false, ref hasLoggedFirstPayload);
                    continue;
                }

                fragmentedPayload = new List<byte>(frame.Value.Payload.Length);
                AppendFragment(fragmentedPayload, frame.Value.Payload);
                continue;
            }

            if (frame.Value.Opcode == 0x0)
            {
                if (fragmentedPayload is null)
                {
                    throw new IOException("Log WebSocket continuation frame has no initial frame");
                }

                AppendFragment(fragmentedPayload, frame.Value.Payload);
                if (!frame.Value.IsFinal)
                {
                    continue;
                }

                payloadCount++;
                var payload = fragmentedPayload.ToArray();
                fragmentedPayload = null;
                var parsed = Publish(Encoding.UTF8.GetString(payload));
                messageCount += parsed;
                LogFirstPayload(generation, attempt, payload.Length, parsed, fragmented: true, ref hasLoggedFirstPayload);
            }
        }

        return new StreamEnd("canceled", payloadCount, messageCount, Stopwatch.GetElapsedTime(startedAt));
    }

    private static void AppendFragment(List<byte> buffer, byte[] payload)
    {
        if (buffer.Count > MaxFrameBytes - payload.Length)
        {
            throw new IOException("Log WebSocket fragmented message is too large");
        }

        buffer.AddRange(payload);
    }

    private static void LogFirstPayload(
        long generation,
        int attempt,
        int byteCount,
        int messageCount,
        bool fragmented,
        ref bool hasLoggedFirstPayload)
    {
        if (hasLoggedFirstPayload)
        {
            return;
        }

        hasLoggedFirstPayload = true;
        AppLogger.Info(
            $"Service-mode core log stream received first payload: generation={generation} attempt={attempt} bytes={byteCount} messages={messageCount} fragmented={fragmented.ToString().ToLowerInvariant()}");
    }

    private static bool ShouldLogAttempt(int attempt) => attempt <= 5 || attempt % 30 == 0;

    private static async Task WriteHandshakeAsync(Stream stream, CancellationToken cancellationToken)
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var request = string.Join(
            "\r\n",
            "GET /logs?level=debug HTTP/1.1",
            "Host: mihomo",
            "Upgrade: websocket",
            "Connection: Upgrade",
            $"Sec-WebSocket-Key: {key}",
            "Sec-WebSocket-Version: 13",
            "\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request), cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReadHandshakeAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new List<byte>(1024);
        var single = new byte[1];
        while (buffer.Count < 8192)
        {
            var read = await stream.ReadAsync(single, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new IOException("Log WebSocket handshake ended early");
            }

            buffer.Add(single[0]);
            if (buffer.Count >= 4
                && buffer[^4] == '\r'
                && buffer[^3] == '\n'
                && buffer[^2] == '\r'
                && buffer[^1] == '\n')
            {
                var response = Encoding.ASCII.GetString(buffer.ToArray());
                if (!response.StartsWith("HTTP/1.1 101 ", StringComparison.OrdinalIgnoreCase)
                    && !response.StartsWith("HTTP/1.0 101 ", StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("Log WebSocket handshake failed");
                }

                return;
            }
        }

        throw new IOException("Log WebSocket handshake response is too large");
    }

    private static async Task<WebSocketFrame?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[2];
        var headerRead = await ReadExactOrEndAsync(stream, header, cancellationToken).ConfigureAwait(false);
        if (!headerRead)
        {
            return null;
        }

        var isFinal = (header[0] & 0x80) != 0;
        var opcode = header[0] & 0x0F;
        var isMasked = (header[1] & 0x80) != 0;
        var length = header[1] & 0x7F;
        if (length == 126)
        {
            var extended = new byte[2];
            await ReadExactAsync(stream, extended, cancellationToken).ConfigureAwait(false);
            length = BinaryPrimitives.ReadUInt16BigEndian(extended);
        }
        else if (length == 127)
        {
            var extended = new byte[8];
            await ReadExactAsync(stream, extended, cancellationToken).ConfigureAwait(false);
            var longLength = BinaryPrimitives.ReadUInt64BigEndian(extended);
            if (longLength > MaxFrameBytes)
            {
                throw new IOException("Log WebSocket frame is too large");
            }

            length = (int)longLength;
        }

        if (length > MaxFrameBytes)
        {
            throw new IOException("Log WebSocket frame is too large");
        }

        var mask = Array.Empty<byte>();
        if (isMasked)
        {
            mask = new byte[4];
            await ReadExactAsync(stream, mask, cancellationToken).ConfigureAwait(false);
        }

        var payload = new byte[length];
        await ReadExactAsync(stream, payload, cancellationToken).ConfigureAwait(false);
        if (isMasked)
        {
            for (var index = 0; index < payload.Length; index++)
            {
                payload[index] ^= mask[index % 4];
            }
        }

        return new WebSocketFrame(isFinal, opcode, payload);
    }

    private static async Task WriteClientFrameAsync(Stream stream, int opcode, byte[] payload, CancellationToken cancellationToken)
    {
        if (payload.Length > 125)
        {
            payload = payload[..125];
        }

        var mask = RandomNumberGenerator.GetBytes(4);
        var frame = new byte[2 + 4 + payload.Length];
        frame[0] = (byte)(0x80 | opcode);
        frame[1] = (byte)(0x80 | payload.Length);
        mask.CopyTo(frame.AsSpan(2));
        for (var index = 0; index < payload.Length; index++)
        {
            frame[6 + index] = (byte)(payload[index] ^ mask[index % 4]);
        }

        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> ReadExactOrEndAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return offset == 0 ? false : throw new IOException("Log WebSocket frame ended early");
            }

            offset += read;
        }

        return true;
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        if (!await ReadExactOrEndAsync(stream, buffer, cancellationToken).ConfigureAwait(false))
        {
            throw new IOException("Log WebSocket frame ended early");
        }
    }

    private int Publish(string line)
    {
        if (_isDisposed)
        {
            return 0;
        }

        var messages = _parser.Parse(line, DateTimeOffset.Now);
        foreach (var message in messages)
        {
            MessageReceived?.Invoke(this, message);
        }

        return messages.Count;
    }

    private static async Task<Stream> ConnectStreamAsync(string pipeName, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
            return pipe;
        }

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            if (!Path.IsPathRooted(pipeName))
            {
                throw new InvalidOperationException("Core Unix socket path must be absolute.");
            }

            await socket.ConnectAsync(new UnixDomainSocketEndPoint(pipeName), cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static string NormalizeEndpoint(string pipePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return pipePath;
        }

        const string prefix = @"\\.\pipe\";
        return pipePath.StartsWith(prefix, StringComparison.Ordinal) ? pipePath[prefix.Length..] : pipePath;
    }

    private readonly record struct WebSocketFrame(bool IsFinal, int Opcode, byte[] Payload);

    private readonly record struct StreamEnd(string Reason, int PayloadCount, int MessageCount, TimeSpan Elapsed);
}
