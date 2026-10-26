#if DEBUG
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;

namespace Stelliberty.Desktop.Debug;

internal static partial class DebugCommands
{
    private static WindowAppearanceProbe? _appearanceProbe;

    private static bool IsAppCommand(string command)
    {
        return string.Equals(command, "screenshot.take", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "clipboard.read", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "toast.state", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "app.memory", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "app.quit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "window.state", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "window.minimize", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "window.maximize", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "window.restore", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "window.close", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "window.show", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "window.appearance.state", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "window.appearance.stop", StringComparison.OrdinalIgnoreCase)
            || command.StartsWith("window.appearance.probe ", StringComparison.OrdinalIgnoreCase)
            || command.StartsWith("window.move ", StringComparison.OrdinalIgnoreCase)
            || command.StartsWith("window.resize ", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string?> ExecuteAppCommandAsync(MainWindow window, string command)
    {
        if (string.Equals(command, "window.appearance.state", StringComparison.OrdinalIgnoreCase))
        {
            return JsonSerializer.Serialize(WindowAppearanceProbe.Capture(window));
        }

        if (string.Equals(command, "window.appearance.stop", StringComparison.OrdinalIgnoreCase))
        {
            return _appearanceProbe?.Stop() ?? throw new InvalidOperationException("No appearance probe has started");
        }

        if (command.StartsWith("window.appearance.probe ", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(command["window.appearance.probe ".Length..], out var durationMs)
                || durationMs is < 200 or > 300_000)
            {
                throw new InvalidOperationException("Appearance probe duration must be 200..300000 ms");
            }

            _appearanceProbe?.Stop("replaced");
            _appearanceProbe = new WindowAppearanceProbe(window, durationMs);
            return "started";
        }

        if (string.Equals(command, "screenshot.take", StringComparison.OrdinalIgnoreCase))
        {
            WindowScreenshot.Save(window);
            return null;
        }

        if (string.Equals(command, "clipboard.read", StringComparison.OrdinalIgnoreCase))
        {
            return window.Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() ?? string.Empty : string.Empty;
        }

        if (string.Equals(command, "toast.state", StringComparison.OrdinalIgnoreCase))
        {
            var viewModel = RequireViewModel(window);
            return $"visible={viewModel.IsToastVisible.ToString().ToLowerInvariant()};type={viewModel.ToastType};message={viewModel.ToastMessage}";
        }

        if (string.Equals(command, "app.memory", StringComparison.OrdinalIgnoreCase))
        {
            return MemoryStateText(window);
        }

        if (string.Equals(command, "app.quit", StringComparison.OrdinalIgnoreCase))
        {
            window.RequestShutdown();
            return null;
        }

        if (string.Equals(command, "window.state", StringComparison.OrdinalIgnoreCase))
        {
            return WindowStateText(window);
        }

        if (string.Equals(command, "window.minimize", StringComparison.OrdinalIgnoreCase))
        {
            window.WindowState = WindowState.Minimized;
            return WindowStateText(window);
        }

        if (string.Equals(command, "window.maximize", StringComparison.OrdinalIgnoreCase))
        {
            window.WindowState = WindowState.Maximized;
            return WindowStateText(window);
        }

        if (string.Equals(command, "window.restore", StringComparison.OrdinalIgnoreCase))
        {
            window.WindowState = WindowState.Normal;
            // Windows 会先恢复最小化前的最大化状态，需同步原生状态后再还原。
            if (OperatingSystem.IsWindows() && window.WindowState == WindowState.Maximized)
            {
                window.WindowState = WindowState.Maximized;
                window.WindowState = WindowState.Normal;
            }
            return WindowStateText(window);
        }

        if (command.StartsWith("window.move ", StringComparison.OrdinalIgnoreCase))
        {
            MoveWindow(window, command["window.move ".Length..].Trim());
            return null;
        }

        if (command.StartsWith("window.resize ", StringComparison.OrdinalIgnoreCase))
        {
            ResizeWindow(window, command["window.resize ".Length..].Trim());
            return WindowStateText(window);
        }

        if (string.Equals(command, "window.close", StringComparison.OrdinalIgnoreCase))
        {
            window.Close();
            return null;
        }

        if (string.Equals(command, "window.show", StringComparison.OrdinalIgnoreCase))
        {
            var wasMinimized = window.WindowState == WindowState.Minimized;
            window.Show();
            if (wasMinimized)
            {
                window.WindowState = WindowState.Normal;
            }
            window.Activate();
            return null;
        }

        throw new InvalidOperationException($"Unknown app command: {command}");
    }

    private static string WindowStateText(MainWindow window)
    {
        var scale = window.RenderScaling <= 0 ? 1 : window.RenderScaling;
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        var workingArea = screen?.WorkingArea;
        return string.Join(
            ';',
            $"visible={window.IsVisible.ToString().ToLowerInvariant()}",
            $"state={window.WindowState}",
            $"x={window.Position.X}",
            $"y={window.Position.Y}",
            $"width={window.Bounds.Width.ToString("0.###", CultureInfo.InvariantCulture)}",
            $"height={window.Bounds.Height.ToString("0.###", CultureInfo.InvariantCulture)}",
            $"scale={scale.ToString("0.###", CultureInfo.InvariantCulture)}",
            $"screenX={workingArea?.X.ToString(CultureInfo.InvariantCulture) ?? string.Empty}",
            $"screenY={workingArea?.Y.ToString(CultureInfo.InvariantCulture) ?? string.Empty}",
            $"screenWidth={workingArea?.Width.ToString(CultureInfo.InvariantCulture) ?? string.Empty}",
            $"screenHeight={workingArea?.Height.ToString(CultureInfo.InvariantCulture) ?? string.Empty}");
    }

    private static string MemoryStateText(MainWindow window)
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var memory = GC.GetGCMemoryInfo();
        var controls = window.GetVisualDescendants().OfType<Control>().Count() + 1;
        return string.Join(
            ';',
            $"private_mb={ToMegabytes(process.PrivateMemorySize64)}",
            $"working_set_mb={ToMegabytes(process.WorkingSet64)}",
            $"managed_mb={ToMegabytes(GC.GetTotalMemory(false))}",
            $"gc_heap_mb={ToMegabytes(memory.HeapSizeBytes)}",
            $"gc_committed_mb={ToMegabytes(memory.TotalCommittedBytes)}",
            $"controls={controls}");
    }

    private static string ToMegabytes(long bytes)
        => (bytes / 1024d / 1024d).ToString("0.0", CultureInfo.InvariantCulture);

    private static void MoveWindow(MainWindow window, string spec)
    {
        var parts = spec.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
        {
            throw new InvalidOperationException("Window move arguments must be x y");
        }

        window.Position = new PixelPoint(x, y);
    }

    // 目标尺寸小于 MinWidth/MinHeight 时窗口停在下限，用于验证最小宽度下的布局。
    private static void ResizeWindow(MainWindow window, string spec)
    {
        var parts = spec.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var width)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var height)
            || width <= 0
            || height <= 0)
        {
            throw new InvalidOperationException("Window resize arguments must be width height");
        }

        window.WindowState = WindowState.Normal;
        window.Width = width;
        window.Height = height;
        window.UpdateLayout();
    }
}
#endif
