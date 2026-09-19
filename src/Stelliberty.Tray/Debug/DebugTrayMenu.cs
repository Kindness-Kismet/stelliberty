#if DEBUG
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stelliberty.Infrastructure.Tray;

namespace Stelliberty.Tray;

internal sealed class DebugTrayMenu(TrayIcon icon, Func<IReadOnlyDictionary<NativeMenuItem, string>> getAutomationIds)
{
    private Window? _window;

    public Task<object> ExecuteAsync(TrayMenuDebugRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Self-drawn tray menu inspection requires Windows.");
        }
        return Dispatcher.UIThread.InvokeAsync(() => ExecuteOnUiThreadAsync(request));
    }

    private async Task<object> ExecuteOnUiThreadAsync(TrayMenuDebugRequest request)
    {
        switch (request.Action)
        {
            case "show":
                Close();
                // Avalonia 未公开托盘右键入口，Debug 调用同一实现以观察真实菜单窗口。
                var implementation = typeof(TrayIcon).GetProperty("Impl", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(icon)!;
                var show = implementation.GetType().GetMethod("OnRightClicked", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new NotSupportedException("This Avalonia tray implementation has no self-drawn menu entry.");
                show.Invoke(implementation, null);
                var desktop = (IClassicDesktopStyleApplicationLifetime)Avalonia.Application.Current!.ApplicationLifetime!;
                _window = desktop.Windows.Single(window => window.Name == $"AvaloniaTrayPopupRoot_{icon.ToolTipText}");
                AutomationProperties.SetAutomationId(_window, "Tray.MenuWindow");
                break;
            case "expand":
                var parent = FindItem(request.AutomationId);
                if (!parent.HasSubMenu || !parent.IsEnabled)
                {
                    throw new InvalidOperationException("Tray menu item cannot expand.");
                }
                parent.Open();
                break;
            case "reveal":
                FindItem(request.AutomationId).BringIntoView();
                break;
            case "click":
                var item = FindItem(request.AutomationId);
                if (item.HasSubMenu || !item.IsEnabled)
                {
                    throw new InvalidOperationException("Tray menu item cannot be activated.");
                }
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Close();
                return new { Activated = request.AutomationId, IsOpen = false };
            case "close":
                Close();
                return new { IsOpen = false };
            case "inspect":
            case "screenshot":
                break;
            default:
                throw new InvalidOperationException($"Unknown tray menu action: {request.Action}");
        }

        await Dispatcher.UIThread.InvokeAsync(() => _window?.UpdateLayout(), DispatcherPriority.Background);
        var items = GetItems().ToArray();
        if (request.Action == "screenshot")
        {
            return SaveScreenshots(items);
        }
        return new
        {
            IsOpen = _window?.IsVisible == true,
            AutomationId = "Tray.MenuWindow",
            Items = items.Select(item => new
            {
                AutomationId = AutomationProperties.GetAutomationId(item),
                ParentId = item.Parent is MenuItem parentItem ? AutomationProperties.GetAutomationId(parentItem) : "Tray.MenuWindow",
                Text = item.Header?.ToString(),
                item.HasSubMenu,
                item.IsSubMenuOpen,
                item.IsChecked,
                item.IsEnabled,
                item.Bounds,
                HeaderParts = item.GetVisualDescendants().OfType<TextBlock>()
                    .Where(text => !string.IsNullOrWhiteSpace(AutomationProperties.GetAutomationId(text)))
                    .Select(text => new
                    {
                        AutomationId = AutomationProperties.GetAutomationId(text),
                        text.Text,
                        text.Bounds,
                        Foreground = text.Foreground?.ToString(),
                        TextTrimming = text.TextTrimming.ToString(),
                    }).ToArray(),
            }).ToArray(),
        };
    }

    private MenuItem FindItem(string? automationId) => GetItems().SingleOrDefault(item =>
        AutomationProperties.GetAutomationId(item) == automationId)
        ?? throw new InvalidOperationException($"Tray menu control not found: {automationId}");

    private IEnumerable<MenuItem> GetItems()
    {
        if (_window?.IsVisible != true || _window.Content is not ItemsControl presenter)
        {
            return [];
        }
        return Visit(presenter, getAutomationIds());
    }

    private static IEnumerable<MenuItem> Visit(ItemsControl parent, IReadOnlyDictionary<NativeMenuItem, string> ids)
    {
        foreach (var item in parent.GetRealizedContainers().OfType<MenuItem>())
        {
            if (item.DataContext is NativeMenuItem native && ids.TryGetValue(native, out var id))
            {
                AutomationProperties.SetAutomationId(item, id);
                yield return item;
            }
            if (item.IsSubMenuOpen)
            {
                foreach (var child in Visit(item, ids)) yield return child;
            }
        }
    }

    private object SaveScreenshots(IReadOnlyList<MenuItem> items)
    {
        if (_window?.IsVisible != true)
        {
            throw new InvalidOperationException("Tray menu window is not open.");
        }
        var roots = new List<Visual> { _window };
        foreach (var item in items.Where(item => item.IsSubMenuOpen))
        {
            if (item.ItemsPanelRoot is { } panel && TopLevel.GetTopLevel(panel) is { } root && !roots.Contains(root)) roots.Add(root);
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }
        var projectRoot = directory?.FullName ?? throw new DirectoryNotFoundException("Debug project root was not found.");
        var screenshotDirectory = Path.Combine(projectRoot, "build", "screenshots");
        Directory.CreateDirectory(screenshotDirectory);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        var paths = new List<string>();
        for (var index = 0; index < roots.Count; index++)
        {
            var root = roots[index];
            var size = new PixelSize((int)Math.Ceiling(root.Bounds.Width), (int)Math.Ceiling(root.Bounds.Height));
            var path = Path.Combine(screenshotDirectory, $"stelliberty-tray-{stamp}-{index}.png");
            // 按 Avalonia 的 96 DPI 逻辑坐标保存每个实际弹层。
            using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
            bitmap.Render(root);
            bitmap.Save(path, PngBitmapEncoderOptions.Default);
            paths.Add(path);
        }
        return new { Files = paths };
    }

    private void Close()
    {
        _window?.Close();
        _window = null;
    }
}
#endif
