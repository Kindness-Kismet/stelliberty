using Avalonia;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stelliberty.Application.Diagnostics;

namespace Stelliberty.Desktop;

public sealed partial class MainWindow
{
    private readonly DispatcherTimer _hiddenReleaseTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private Control? _hibernatedShell;
    private bool _shellDataContextWasInherited;
    private long _hiddenVersion;

    private void CancelHiddenRelease()
    {
        _hiddenReleaseTimer.Stop();
        Interlocked.Increment(ref _hiddenVersion);
    }

    private async void OnHiddenReleaseTimerTickAsync(object? sender, EventArgs args)
    {
        _hiddenReleaseTimer.Stop();
        if (IsVisible || _isShutdownRequested || _isShutdownPreparing || _hibernatedShell is not null)
        {
            return;
        }

        var version = Volatile.Read(ref _hiddenVersion);
        try
        {
            var compositor = SuspendHiddenShell();
            if (compositor is null)
            {
                return;
            }

            await compositor.RequestCommitAsync();
            if (version != Volatile.Read(ref _hiddenVersion) || _isShutdownRequested || _isShutdownPreparing)
            {
                return;
            }

            // 终结器可能需要 UI 调度，等待期间保持界面线程可用。
            await Task.Run(() =>
            {
                if (version != Volatile.Read(ref _hiddenVersion))
                {
                    return;
                }

                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                if (version == Volatile.Read(ref _hiddenVersion))
                {
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                }
            });
            AppLogger.Debug("Hidden page memory collection completed");
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Hidden page memory release failed");
        }
    }

    private Compositor? SuspendHiddenShell()
    {
        if (Content is not Control shell || ElementComposition.GetElementVisual(shell) is not { } shellVisual)
        {
            return null;
        }

        var parentVisual = shell.GetVisualParent() is { } parent
            ? ElementComposition.GetElementVisual(parent) as CompositionContainerVisual
            : null;
        var visuals = shell.GetVisualDescendants().Prepend(shell)
            .Select(ElementComposition.GetElementVisual)
            .OfType<CompositionContainerVisual>()
            .ToArray();

        _hibernatedShell = shell;
        _shellDataContextWasInherited = !shell.IsSet(DataContextProperty);
        // 脱树期间保留继承的数据源，避免清空页面数据和输入状态。
        if (_shellDataContextWasInherited)
        {
            shell.DataContext = DataContext;
        }

        Content = null;
        Presenter?.UpdateChild();

        // 隐藏时渲染器暂停，只解除已脱树控件的旧合成关系；挂回时由框架重建。
        parentVisual?.Children.Remove(shellVisual);
        foreach (var visual in visuals)
        {
            visual.Children.Clear();
        }

        AppLogger.Debug("Hidden page shell suspended");
        return shellVisual.Compositor;
    }

    private void RestoreHiddenShell()
    {
        if (_hibernatedShell is not { } shell)
        {
            return;
        }

        _hibernatedShell = null;
        // 热重载已创建新内容时保留新树，避免旧休眠内容覆盖它。
        if (Content is not null)
        {
            return;
        }

        Content = shell;
        Presenter?.UpdateChild();
        if (_shellDataContextWasInherited)
        {
            shell.ClearValue(DataContextProperty);
        }
        AppLogger.Debug("Hidden page shell restored");
    }
}
