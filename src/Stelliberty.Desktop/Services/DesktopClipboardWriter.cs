using Avalonia.Controls;
using Avalonia.Input.Platform;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;

namespace Stelliberty.Desktop.Services;

internal sealed class DesktopClipboardWriter : IClipboardWriter
{
    private Window? _window;

    // 静默启动的主窗口不进桌面生命周期，剪贴板只能挂在窗口实例上。
    public void Attach(Window window) => _window = window;

    public void WriteText(string text)
    {
        // 剪贴板占用可能很短；异步写入避免卡住 UI 线程。
        _ = WriteTextCoreAsync(text);
    }

    private async Task WriteTextCoreAsync(string text)
    {
        try
        {
            var clipboard = _window?.Clipboard;
            if (clipboard is null)
            {
                AppLogger.Warning("Clipboard write skipped: window clipboard unavailable");
                return;
            }

            await clipboard.SetTextAsync(text);
            AppLogger.Debug($"Clipboard text written: length={text.Length}");
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Clipboard write failed: {exception.Message}");
        }
    }
}
