#if DEBUG
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Avalonia.Threading;
using Stelliberty.Application.Diagnostics;

namespace Stelliberty.Desktop.Debug;

internal static partial class DebugCommands
{
    // 调试命令端口需与外部调试客户端保持一致。
    private const int Port = 20000;

    public static void Start(MainWindow window)
    {
        _ = Task.Run(() => RunTcpServerAsync(window));
        AppLogger.Info($"Debug control port: 127.0.0.1:{Port}");
    }

    private static async Task RunTcpServerAsync(MainWindow window)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, Port);
            listener.Start();

            while (true)
            {
                using var client = await listener.AcceptTcpClientAsync();
                await HandleClientAsync(client, window);
            }
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Debug control port startup failed");
        }
    }

    private static async Task HandleClientAsync(TcpClient client, MainWindow window)
    {
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream);
        using var writer = new StreamWriter(stream) { AutoFlush = true, NewLine = "\n" };
        var command = (await reader.ReadLineAsync())?.Trim();
        if (string.IsNullOrEmpty(command))
        {
            await writer.WriteLineAsync("ERR empty");
            return;
        }

        try
        {
            var receivedAt = Stopwatch.GetTimestamp();
            if (string.Equals(command, "window.show", StringComparison.OrdinalIgnoreCase))
            {
                AppLogger.Info("[StartupTrace] Debug window.show received");
            }
            var result = await Dispatcher.UIThread.InvokeAsync(() => ExecuteAsync(window, command));
            if (string.Equals(command, "window.show", StringComparison.OrdinalIgnoreCase))
            {
                AppLogger.Info($"[StartupTrace] Debug window.show completed elapsed={Stopwatch.GetElapsedTime(receivedAt).TotalMilliseconds:0.0}ms");
            }
            await writer.WriteLineAsync(result is null ? "OK" : $"OK {result}");
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, $"Command failed: {command}");
            await writer.WriteLineAsync($"ERR {exception.Message}");
        }
    }

    private static Task<string?> ExecuteAsync(MainWindow window, string command)
    {
        if (command.StartsWith("home.", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteHomeCommandAsync(window, command);
        }

        if (command.StartsWith("proxies.", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteProxiesCommandAsync(window, command);
        }

        if (command.StartsWith("connections.", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteConnectionsCommandAsync(window, command);
        }

        if (command.StartsWith("core-logs.", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteCoreLogsCommandAsync(window, command);
        }

        if (command.StartsWith("rules.", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteRulesCommandAsync(window, command);
        }

        if (command.StartsWith("subscriptions.", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteSubscriptionsCommandAsync(window, command);
        }

        if (command.StartsWith("overrides.", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteOverridesCommandAsync(window, command);
        }

        if (command.StartsWith("settings.", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteSettingsCommandAsync(window, command);
        }

        if (command.StartsWith("clash.", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteClashCommandAsync(window, command);
        }

        if (command.StartsWith("core.", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteCoreCommandAsync(window, command);
        }

        if (command.StartsWith("service.", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteServiceCommandAsync(window, command);
        }

        if (IsAppCommand(command))
        {
            return ExecuteAppCommandAsync(window, command);
        }

        if (command.StartsWith("page.", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteNavigationCommandAsync(window, command);
        }

        if (command.StartsWith("control.", StringComparison.OrdinalIgnoreCase)
            || command.StartsWith("dropdown.", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(ExecuteControlCommand(window, command));
        }

        if (command.StartsWith("keyboard.", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(ExecuteKeyboardCommand(window, command));
        }

        if (command.StartsWith("hotkey.trigger ", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteHotkeyCommandAsync(window, command);
        }

        throw new InvalidOperationException($"Unknown command: {command}");
    }
}
#endif
