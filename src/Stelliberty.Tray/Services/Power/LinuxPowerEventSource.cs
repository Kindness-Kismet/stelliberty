using System.Runtime.Versioning;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Runtime;
using Tmds.DBus.Protocol;

namespace Stelliberty.Tray;

[SupportedOSPlatform("linux")]
internal sealed class LinuxPowerEventSource(Action<SystemPowerEventKind, string> report) : ISystemPowerEventSource
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private readonly CancellationTokenSource _stopping = new();
    private Task? _runTask;
    private string _status = "not-started";

    public string Status => Volatile.Read(ref _status);

    public Task StartAsync()
    {
        _runTask = Task.Run(RunAsync);
        return Task.CompletedTask;
    }

    private async Task RunAsync()
    {
        var token = _stopping.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    Volatile.Write(ref _status, "linux-login1-connecting");
                    using var connection = new DBusConnection(DBusAddress.System
                        ?? throw new InvalidOperationException("The D-Bus system address is unavailable."));
                    await connection.ConnectAsync().AsTask().WaitAsync(ConnectTimeout, token).ConfigureAwait(false);
                    var hasLogind = await connection.CallMethodAsync(CreateOwnerQuery(connection),
                        static (message, _) => message.GetBodyReader().ReadBool()).WaitAsync(ConnectTimeout, token).ConfigureAwait(false);
                    if (!hasLogind) throw new InvalidOperationException("The system bus has no org.freedesktop.login1 service.");

                    using var subscription = await connection.AddMatchAsync(
                        new MatchRule
                        {
                            Type = MessageType.Signal,
                            Sender = "org.freedesktop.login1",
                            Path = "/org/freedesktop/login1",
                            Interface = "org.freedesktop.login1.Manager",
                            Member = "PrepareForSleep",
                        },
                        static (message, _) => message.GetBodyReader().ReadBool(),
                        handler: notification =>
                        {
                            if (!notification.HasValue)
                            {
                                AppLogger.Warning("Power signal failed: platform=linux detail=notification-completed");
                                return;
                            }
                            report(notification.Value ? SystemPowerEventKind.Suspend : SystemPowerEventKind.Resume, "linux.login1");
                        },
                        emitOnCapturedContext: false, flags: ObserverFlags.EmitOnReaderFailed)
                        .AsTask().WaitAsync(ConnectTimeout, token).ConfigureAwait(false);
                    Volatile.Write(ref _status, "linux-login1-active");
                    AppLogger.Info("Power monitor ready: platform=linux source=login1.PrepareForSleep");
                    var disconnected = await connection.DisconnectedAsync().WaitAsync(token).ConfigureAwait(false);
                    throw disconnected ?? new IOException("System bus disconnected.");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    Volatile.Write(ref _status, $"linux-login1-unavailable: {exception.Message}");
                    AppLogger.Warning($"Power monitor reconnect scheduled: platform=linux delay_s={ReconnectDelay.TotalSeconds:0} detail={exception.Message}");
                }
                // 系统总线重启后，旧订阅已失效，必须重新建立匹配规则。
                await Task.Delay(ReconnectDelay, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private static MessageBuffer CreateOwnerQuery(DBusConnection connection)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: "org.freedesktop.DBus", path: "/org/freedesktop/DBus",
            @interface: "org.freedesktop.DBus", member: "NameHasOwner", signature: "s");
        writer.WriteString("org.freedesktop.login1");
        return writer.CreateMessage();
    }

    public async ValueTask DisposeAsync()
    {
        _stopping.Cancel();
        if (_runTask is not null) await _runTask.ConfigureAwait(false);
        _stopping.Dispose();
        Volatile.Write(ref _status, "stopped");
    }
}
