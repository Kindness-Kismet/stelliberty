using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;

namespace Stelliberty.Tray;

internal sealed class TrayClipboard : IDisposable
{
    private Window? _window;

    public async Task WriteTextAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            // 后台剪贴板依赖宿主窗口句柄，不加载业务界面。
            if (_window is null)
            {
                _window = new Window { ShowInTaskbar = false };
                AutomationProperties.SetAutomationId(_window, "Tray.ClipboardHost");
            }
            var clipboard = _window.Clipboard
                ?? throw new InvalidOperationException("Tray clipboard is unavailable.");
            await clipboard.SetTextAsync(text);
        });
    }

    public void Dispose()
    {
        _window?.Close();
        _window = null;
    }
}
