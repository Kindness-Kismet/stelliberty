#if DEBUG
using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using IconPacks.Avalonia.MingCuteIcons;

namespace Stelliberty.Desktop.Debug;

internal static partial class DebugCommands
{
    // 隐藏窗口不再提交动画帧，调试观察最多等待 2 秒。
    private static readonly TimeSpan FrameReadyTimeout = TimeSpan.FromSeconds(2);

    private sealed record ControlMotionFrame(
        double ElapsedMs,
        double X,
        double Y,
        double Width,
        double Height,
        double Opacity,
        double EffectiveOpacity,
        bool? IsChecked,
        string? Foreground,
        string Classes,
        string? IconKind);

    private static ControlMotionFrame CaptureControlMotion(MainWindow window, Control control, long startedAt)
    {
        var origin = control.TranslatePoint(new Point(0, 0), window)
            ?? throw new InvalidOperationException("Control is not attached to the requested window");
        var icon = control.GetVisualDescendants().OfType<PackIconMingCuteIcons>().FirstOrDefault();
        var effectiveOpacity = control.Opacity;
        foreach (var ancestor in control.GetVisualAncestors().OfType<Control>())
        {
            effectiveOpacity *= ancestor.Opacity;
        }

        return new ControlMotionFrame(
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
            origin.X,
            origin.Y,
            control.Bounds.Width,
            control.Bounds.Height,
            control.Opacity,
            effectiveOpacity,
            (control as ToggleButton)?.IsChecked,
            (control as TemplatedControl)?.Foreground?.ToString(),
            string.Join(' ', control.Classes),
            icon?.Kind.ToString());
    }

    private static async Task<string?> TraceControlTransitionAsync(MainWindow window, string spec)
    {
        var parts = spec.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || !int.TryParse(parts[2], out var durationMs) || durationMs is < 100 or > 10_000)
        {
            throw new InvalidOperationException("control.transition usage: <observe_id> <click_id> <duration_ms:100..10000>");
        }

        if (!window.IsVisible)
        {
            throw new InvalidOperationException("Animation tracing requires a visible window");
        }

        var control = FindControlByAutomationId(window, parts[0]);
        var startedAt = Stopwatch.GetTimestamp();
        var frames = new List<ControlMotionFrame> { CaptureControlMotion(window, control, startedAt) };
        ClickControl(window, parts[1]);
        while (Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds < durationMs)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.RequestAnimationFrame(_ => completion.SetResult());
            try
            {
                await completion.Task.WaitAsync(FrameReadyTimeout);
            }
            catch (TimeoutException exception)
            {
                throw new TimeoutException("Window did not produce an animation frame within 2 seconds", exception);
            }

            frames.Add(CaptureControlMotion(window, control, startedAt));
        }

        return JsonSerializer.Serialize(frames);
    }
}
#endif
