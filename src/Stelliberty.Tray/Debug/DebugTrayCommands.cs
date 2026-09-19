#if DEBUG
using System.Diagnostics;
using System.Text.Json;
using Stelliberty.Infrastructure.Tray;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Runtime;

namespace Stelliberty.Tray;

internal static class DebugTrayCommands
{
    public static async Task<int> ExecuteAsync(string command)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await using var client = new TrayIpcClient();
            await client.ConnectAsync(timeout.Token).ConfigureAwait(false);
            var hello = await client.HelloAsync(Environment.ProcessId, timeout.Token).ConfigureAwait(false);
            using var process = Process.GetProcessById(hello.TrayPid);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            // 调试命令只允许控制相同发布目录中的托盘进程。
            if (!string.Equals(process.MainModule?.FileName, Environment.ProcessPath, comparison))
            {
                throw new InvalidOperationException("Tray process belongs to another application directory.");
            }

            object? result;
            switch (command)
            {
                case "state":
                    result = await client.GetHealthAsync(timeout.Token).ConfigureAwait(false);
                    break;
                case "show-ui":
                    result = await client.ActivateUiAsync(Environment.ProcessId, timeout.Token).ConfigureAwait(false);
                    break;
                case "toggle-window":
                    result = await client.SimulateGlobalHotkeyAsync(GlobalHotkeyAction.ToggleWindow, timeout.Token).ConfigureAwait(false);
                    break;
                case "copy-terminal":
                    await client.CopyTerminalProxyAsync(timeout.Token).ConfigureAwait(false);
                    result = null;
                    break;
                case "proxy-menu":
                    result = await client.GetProxyMenuAsync(timeout.Token).ConfigureAwait(false);
                    break;
                case "power-suspend":
                    result = await client.SimulatePowerEventAsync(SystemPowerEventKind.Suspend, timeout.Token).ConfigureAwait(false);
                    break;
                case "power-resume":
                    result = await client.SimulatePowerEventAsync(SystemPowerEventKind.Resume, timeout.Token).ConfigureAwait(false);
                    break;
                case "core-stop":
                    result = await client.StopCoreAsync(timeout.Token).ConfigureAwait(false);
                    break;
                case "core-start":
                    result = await client.EnsureCoreStartedAsync(timeout.Token).ConfigureAwait(false);
                    break;
                case var menu when menu.StartsWith("menu ", StringComparison.Ordinal):
                    var menuRequest = JsonSerializer.Deserialize<TrayMenuDebugRequest>(menu["menu ".Length..])
                        ?? throw new InvalidOperationException("Missing tray menu command.");
                    result = await client.ExecuteMenuDebugAsync(menuRequest, timeout.Token).ConfigureAwait(false);
                    break;
                case var selection when selection.StartsWith("select-proxy ", StringComparison.Ordinal):
                    var request = JsonSerializer.Deserialize<Stelliberty.Domain.Proxies.ProxyChangeRequest>(selection["select-proxy ".Length..])
                        ?? throw new InvalidOperationException("Missing proxy selection.");
                    await client.SelectProxyAsync(request, timeout.Token).ConfigureAwait(false);
                    result = await client.GetProxyMenuAsync(timeout.Token).ConfigureAwait(false);
                    break;
                case "stop":
                    await client.ShutdownAsync(timeout.Token).ConfigureAwait(false);
                    result = null;
                    break;
                default:
                    throw new InvalidOperationException($"Unknown tray debug command: {command}");
            }
            Console.WriteLine(JsonSerializer.Serialize(result));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}
#endif
