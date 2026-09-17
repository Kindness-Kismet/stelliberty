using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Stelliberty.Application.Diagnostics;

namespace Stelliberty.Infrastructure.Tray;

public sealed class TrayIpcServer : IAsyncDisposable
{
    // 配置按内容传递，帧上限与核心 IPC 的 4 MiB 限制保持一致。
    public const int MaxFrameBytes = 4 * 1024 * 1024;

    private readonly string _endpoint;
    private readonly Func<TrayIpcConnection, TrayIpcRequest, CancellationToken, Task<TrayIpcResult>> _requestHandler;
    private readonly Func<Guid, Task> _connectionClosed;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly ConcurrentDictionary<Guid, Task> _connections = new();
    private readonly object _listenerGate = new();
    private NamedPipeServerStream? _pendingPipe;
    private Socket? _listener;
    private string? _socketPath;
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;

    public TrayIpcServer(
        string endpoint,
        Func<TrayIpcConnection, TrayIpcRequest, CancellationToken, Task<TrayIpcResult>> requestHandler,
        Func<Guid, Task> connectionClosed)
    {
        _endpoint = endpoint;
        _requestHandler = requestHandler;
        _connectionClosed = connectionClosed;
        PrepareListener();
    }

    public Task Completion => _runTask ?? Task.CompletedTask;

    public void Start(CancellationToken cancellationToken)
    {
        if (_runTask is not null)
        {
            throw new InvalidOperationException("Tray IPC server is already running.");
        }

        _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposeCancellation.Token);
        _runTask = RunAsync(_runCancellation.Token);
    }

    private void PrepareListener()
    {
        if (OperatingSystem.IsWindows())
        {
            _pendingPipe = CreatePipe();
            return;
        }

        TrayEndpoint.PrepareRuntimeDirectory();
        _socketPath = _endpoint;
        if (File.Exists(_socketPath))
        {
            File.Delete(_socketPath);
        }

        _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
        _listener.Listen(16);
        File.SetUnixFileMode(_socketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                await RunWindowsAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await RunUnixAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            await WaitForConnectionsAsync().ConfigureAwait(false);
        }
    }

    private async Task RunWindowsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            lock (_listenerGate)
            {
                pipe = _pendingPipe ?? throw new ObjectDisposedException(nameof(TrayIpcServer));
            }

            await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            lock (_listenerGate)
            {
                if (ReferenceEquals(_pendingPipe, pipe))
                {
                    _pendingPipe = CreatePipe();
                }
            }

            TrackConnection(pipe, cancellationToken);
        }
    }

    private async Task RunUnixAsync(CancellationToken cancellationToken)
    {
        var listener = _listener ?? throw new ObjectDisposedException(nameof(TrayIpcServer));
        while (!cancellationToken.IsCancellationRequested)
        {
            var socket = await listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
            TrackConnection(new NetworkStream(socket, ownsSocket: true), cancellationToken);
        }
    }

    private void TrackConnection(Stream stream, CancellationToken cancellationToken)
    {
        var connection = new TrayIpcConnection(stream);
        var task = HandleConnectionAsync(connection, cancellationToken);
        _connections[connection.Id] = task;
        _ = task.ContinueWith(
            completedTask => _connections.TryRemove(connection.Id, out _),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task HandleConnectionAsync(TrayIpcConnection connection, CancellationToken cancellationToken)
    {
        await using (connection.ConfigureAwait(false))
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var line = await connection.ReadLineAsync(MaxFrameBytes, cancellationToken).ConfigureAwait(false);
                    if (line is null)
                    {
                        return;
                    }

                    var request = ParseRequest(line);
                    var result = await _requestHandler(connection, request, cancellationToken).ConfigureAwait(false);
                    await connection.SendResponseAsync(request.Id, result, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (IOException)
            {
            }
            catch (SocketException)
            {
            }
            catch (TrayIpcFrameTooLargeException)
            {
                AppLogger.Warning("Tray IPC connection closed after an oversized request");
            }
            catch (DecoderFallbackException)
            {
                AppLogger.Warning("Tray IPC rejected invalid UTF-8");
            }
            catch (JsonException exception)
            {
                AppLogger.Warning($"Tray IPC rejected invalid JSON: {exception.Message}");
            }
            catch (Exception exception)
            {
                AppLogger.Error(exception, "Tray IPC connection failed");
            }
            finally
            {
                await _connectionClosed(connection.Id).ConfigureAwait(false);
            }
        }
    }

    private static TrayIpcRequest ParseRequest(string line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        var id = root.GetProperty("id").GetString();
        var method = root.GetProperty("method").GetString();
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(method))
        {
            throw new JsonException("IPC request id and method are required.");
        }

        var parameters = root.TryGetProperty("params", out var value)
            ? value.Clone()
            : JsonSerializer.SerializeToElement(new { });
        return new TrayIpcRequest(id, method, parameters);
    }

    private NamedPipeServerStream CreatePipe()
    {
        return new NamedPipeServerStream(
            _endpoint,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    }

    private async Task WaitForConnectionsAsync()
    {
        var connections = _connections.Values.ToArray();
        if (connections.Length > 0)
        {
            await Task.WhenAll(connections).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposeCancellation.Cancel();
        lock (_listenerGate)
        {
            _pendingPipe?.Dispose();
            _pendingPipe = null;
            _listener?.Dispose();
            _listener = null;
        }

        if (_runTask is not null)
        {
            await _runTask.ConfigureAwait(false);
        }

        _runCancellation?.Dispose();
        _disposeCancellation.Dispose();
        if (_socketPath is not null && File.Exists(_socketPath))
        {
            File.Delete(_socketPath);
        }
    }
}

public sealed class TrayIpcConnection : IAsyncDisposable
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly Stream _stream;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly byte[] _readBuffer = new byte[8192];
    private int _readOffset;
    private int _readLength;

    internal TrayIpcConnection(Stream stream)
    {
        _stream = stream;
        _writer = new StreamWriter(stream, new UTF8Encoding(false), 8192, leaveOpen: true)
        {
            AutoFlush = false,
            NewLine = "\n",
        };
    }

    public Guid Id { get; } = Guid.NewGuid();

    internal async ValueTask<string?> ReadLineAsync(int maxFrameBytes, CancellationToken cancellationToken)
    {
        var line = new ArrayBufferWriter<byte>(Math.Min(_readBuffer.Length, maxFrameBytes));
        while (true)
        {
            if (_readOffset == _readLength)
            {
                _readLength = await _stream.ReadAsync(_readBuffer, cancellationToken).ConfigureAwait(false);
                _readOffset = 0;
                if (_readLength == 0)
                {
                    return line.WrittenCount == 0 ? null : DecodeLine(line.WrittenSpan);
                }
            }

            var unread = _readBuffer.AsSpan(_readOffset, _readLength - _readOffset);
            var newlineIndex = unread.IndexOf((byte)'\n');
            var chunkLength = newlineIndex >= 0 ? newlineIndex : unread.Length;
            if (line.WrittenCount > maxFrameBytes - chunkLength)
            {
                throw new TrayIpcFrameTooLargeException();
            }

            unread[..chunkLength].CopyTo(line.GetSpan(chunkLength));
            line.Advance(chunkLength);
            _readOffset += chunkLength;
            if (newlineIndex < 0)
            {
                continue;
            }

            _readOffset++;
            return DecodeLine(line.WrittenSpan);
        }
    }

    private static string DecodeLine(ReadOnlySpan<byte> bytes)
    {
        if (!bytes.IsEmpty && bytes[^1] == '\r')
        {
            bytes = bytes[..^1];
        }

        return StrictUtf8.GetString(bytes);
    }

    public Task SendEventAsync(string eventName, object data, CancellationToken cancellationToken) =>
        WriteAsync(new { @event = eventName, data }, cancellationToken);

    internal Task SendResponseAsync(string id, TrayIpcResult result, CancellationToken cancellationToken)
    {
        object response = result.IsSuccess
            ? new { id, result = result.Value }
            : new { id, error = new { code = result.ErrorCode, message = result.ErrorMessage } };
        return WriteAsync(response, cancellationToken);
    }

    private async Task WriteAsync(object value, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(value, TrayJson.Options);
        if (Encoding.UTF8.GetByteCount(payload) > TrayIpcServer.MaxFrameBytes)
        {
            throw new InvalidOperationException("Tray IPC response exceeds the frame limit.");
        }

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _writer.WriteLineAsync(payload.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync().ConfigureAwait(false);
        await _stream.DisposeAsync().ConfigureAwait(false);
        _writeGate.Dispose();
    }
}

internal sealed class TrayIpcFrameTooLargeException : Exception
{
}
