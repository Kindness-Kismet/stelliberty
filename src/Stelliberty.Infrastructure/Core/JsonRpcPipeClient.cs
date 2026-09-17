using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Stelliberty.Application.Diagnostics;

namespace Stelliberty.Infrastructure.Core;

// JSON Lines 的 id 用于关联请求；无 id 帧是事件。
public sealed class JsonRpcPipeClient : IDisposable, IAsyncDisposable
{
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly string _pipeName;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new(StringComparer.Ordinal);
    private Stream? _stream;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource? _readerCts;
    private long _idSeed;
    private bool _isDisposed;

    public JsonRpcPipeClient(string pipeName) => _pipeName = pipeName;

    public event EventHandler<EventNotification>? EventReceived;

    public event EventHandler? Disconnected;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        _stream = await ConnectStreamAsync(_pipeName, cancellationToken).ConfigureAwait(false);
        _reader = new StreamReader(_stream, Encoding.UTF8, false, 8192, leaveOpen: true);
        _writer = new StreamWriter(_stream, new UTF8Encoding(false)) { AutoFlush = false, NewLine = "\n" };
        _readerCts = new CancellationTokenSource();
        _ = Task.Run(() => ReadLoopAsync(_readerCts.Token));
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
                throw new InvalidOperationException("IPC Unix socket path must be absolute.");
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

    public async Task<JsonElement> RequestAsync(string method, JsonElement parameters, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_writer is null)
        {
            throw new InvalidOperationException("Pipe is not connected");
        }

        var id = Interlocked.Increment(ref _idSeed).ToString(CultureInfo.InvariantCulture);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        var payload = JsonSerializer.Serialize(new { id, method, @params = parameters });
        try
        {
            // 写入前先登记 pending，避免早到响应丢失匹配。
            // 并发请求共用一条管道，完整帧必须串行写入。
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
            await using var reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _reader is not null)
            {
                var line = await _reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("id", out var idProp))
                {
                    var id = idProp.GetString();
                    if (id is not null && _pending.TryRemove(id, out var tcs))
                    {
                        if (root.TryGetProperty("result", out var result))
                        {
                            tcs.TrySetResult(result.Clone());
                        }
                        else if (root.TryGetProperty("error", out var error))
                        {
                            var code = error.TryGetProperty("code", out var c) ? c.GetString() ?? "unknown" : "unknown";
                            var message = error.TryGetProperty("message", out var m) ? m.GetString() ?? string.Empty : string.Empty;
                            tcs.TrySetException(new IpcRemoteException(code, message));
                        }
                    }
                }
                else if (root.TryGetProperty("event", out var ev) && root.TryGetProperty("data", out var data))
                {
                    try
                    {
                        EventReceived?.Invoke(this, new EventNotification(ev.GetString() ?? string.Empty, data.Clone()));
                    }
                    catch (Exception exception)
                    {
                        // 订阅者异常不能中断读取循环。
                        AppLogger.Error(exception, "IPC event dispatch failed");
                    }
                }
            }

            if (!ct.IsCancellationRequested && !_isDisposed)
            {
                FailPending(new IOException("IPC connection closed before a response was received."));
                NotifyDisconnected();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception) when (_isDisposed)
        {
        }
        catch (Exception ex)
        {
            // 读取循环失败会中断所有待处理调用的响应源；无 pending 时仍需留下第一现场。
            AppLogger.Error(ex, "IPC read loop failed");
            FailPending(ex);
            NotifyDisconnected();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _readerCts?.Cancel();
        _writer?.Dispose();
        _reader?.Dispose();
        _stream?.Dispose();
        _readerCts?.Dispose();
        CancelPending();
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _readerCts?.Cancel();

        if (_writer is not null)
        {
            try
            {
                await _writer.DisposeAsync().ConfigureAwait(false);
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }
        _reader?.Dispose();
        if (_stream is not null)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
        _readerCts?.Dispose();
        CancelPending();
    }

    private void CancelPending()
    {
        foreach (var pending in _pending.Values)
        {
            pending.TrySetCanceled();
        }

        _pending.Clear();
    }

    private void FailPending(Exception exception)
    {
        foreach (var pending in _pending.Values)
        {
            pending.TrySetException(exception);
        }

        _pending.Clear();
    }

    private void NotifyDisconnected()
    {
        try
        {
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "IPC disconnect observer failed");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}

public sealed record EventNotification(string Event, JsonElement Data);

public sealed class IpcRemoteException : Exception
{
    public string Code { get; }

    public IpcRemoteException(string code, string message)
        : base(message)
    {
        Code = code;
    }
}
