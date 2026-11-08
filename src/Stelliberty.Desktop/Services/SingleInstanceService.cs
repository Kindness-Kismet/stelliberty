using System.IO.Pipes;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;

namespace Stelliberty.Desktop.Services;

internal sealed class SingleInstanceService : IDisposable
{
    private static readonly string InstanceName = $"{AppMetadata.Name}.{AppRuntimeNames.ChannelName}.SingleInstance";
    private static readonly string ActivationPipeName = $"{InstanceName}.Activate";
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stopping = new();
    private Task? _listener;

    public SingleInstanceService()
    {
        // 沿用安装程序识别的互斥量，避免升级期间重复启动应用。
        _mutex = new Mutex(true, OperatingSystem.IsWindows() ? $"Global\\{InstanceName}" : InstanceName, out var ownsInstance);
        OwnsInstance = ownsInstance;
    }

    public bool OwnsInstance { get; }

    public void Listen(Action activate) => _listener = ListenAsync(activate);

    private async Task ListenAsync(Action activate)
    {
        while (!_stopping.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(ActivationPipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stopping.Token).ConfigureAwait(false);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(pipe, leaveOpen: true);
                await writer.WriteLineAsync(Environment.ProcessId.ToString()).ConfigureAwait(false);
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
                requestTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                if (await reader.ReadLineAsync(requestTimeout.Token).ConfigureAwait(false) == "activate") activate();
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException)
            {
                AppLogger.Warning($"Single instance activation failed: {exception.Message}");
            }
        }
    }

    public static async Task<int> ActivateExistingAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await using var pipe = new NamedPipeClientStream(".", ActivationPipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            if (!int.TryParse(await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false), out var processId)) return 1;
            ForegroundActivation.AllowForProcess(processId);
            await writer.WriteLineAsync("activate".AsMemory(), timeout.Token).ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
            AppLogger.Warning($"Existing application activation failed: {exception.Message}");
            return 1;
        }
    }

    public void Dispose()
    {
        _stopping.Cancel();
        if (OwnsInstance) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
