#if DEBUG
using System.Globalization;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.VisualTree;
using AppNavigationPage = Stelliberty.Presentation.ViewModels.NavigationPage;
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop.Debug;

internal static partial class DebugCommands
{
    private static async Task<string?> ExecuteNavigationCommandAsync(MainWindow window, string command)
    {
        if (string.Equals(command, "page.state", StringComparison.OrdinalIgnoreCase))
        {
            return JsonSerializer.Serialize(window.CapturePageLoadingState());
        }
        if (command.StartsWith("page.scroll y", StringComparison.OrdinalIgnoreCase))
        {
            return ReadOrSetCurrentPageScrollViewerY(window, command["page.scroll y".Length..].Trim()).ToString("0.###", CultureInfo.InvariantCulture);
        }

        if (command.StartsWith("page.open ", StringComparison.OrdinalIgnoreCase))
        {
            var page = Navigate(window, command["page.open ".Length..].Trim());
            await window.WaitForPageReadyAsync(page);
            return null;
        }

        throw new InvalidOperationException($"Unknown page command: {command}");
    }

    public static async Task DelayPageInitializationAsync()
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("STELLIBERTY_DEBUG_STARTUP_DELAY_MS"), out var delay)
            && delay > 0)
        {
            // 仅调试启动延迟，最多 30 秒；不改变配置、核心或生产版行为。
            await Task.Delay(Math.Min(delay, 30_000));
        }
    }

    private static AppNavigationPage Navigate(MainWindow window, string spec)
    {
        if (window.DataContext is not MainWindowViewModel viewModel)
        {
            throw new InvalidOperationException("DataContext is not ready");
        }

        var page = spec.ToLowerInvariant() switch
        {
            "home" => AppNavigationPage.Home,
            "proxies" => AppNavigationPage.Proxy,
            "connections" => AppNavigationPage.Connections,
            "core-logs" => AppNavigationPage.CoreLogs,
            "rules" => AppNavigationPage.Rules,
            "subscriptions" => AppNavigationPage.Subscriptions,
            "overrides" => AppNavigationPage.Overrides,
            _ => NavigateSettingsPage(viewModel, spec),
        };
        viewModel.CurrentPage = page;
        return page;
    }

    private static AppNavigationPage NavigateSettingsPage(MainWindowViewModel viewModel, string spec)
    {
        viewModel.Settings.SubPage = spec.ToLowerInvariant() switch
        {
            "settings" or "settings/root" => SettingsSubPage.Root,
            "settings/theme" => SettingsSubPage.Theme,
            "settings/language" => SettingsSubPage.Language,
            "settings/clash-features" => SettingsSubPage.ClashFeatures,
            "settings/app-behavior" => SettingsSubPage.AppBehavior,
            "settings/data-management" => SettingsSubPage.DataManagement,
            "settings/update" => SettingsSubPage.Update,
            "settings/about" => SettingsSubPage.About,
            "settings/app-log" => SettingsSubPage.AppLog,
            "settings/network" => SettingsSubPage.Network,
            "settings/port-control" => SettingsSubPage.PortControl,
            "settings/system-integration" => SettingsSubPage.SystemIntegration,
            "settings/dns" => SettingsSubPage.Dns,
            "settings/performance" => SettingsSubPage.Performance,
            "settings/core-log" => SettingsSubPage.CoreLog,
            _ => throw new InvalidOperationException($"Unknown page: {spec}"),
        };
        return AppNavigationPage.Settings;
    }

    private static double ReadOrSetCurrentPageScrollViewerY(MainWindow window, string spec)
    {
        var text = spec.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return FindCurrentPageScrollViewer(window).Offset.Y;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            throw new InvalidOperationException("page.scroll y usage: page.scroll y [y]");
        }

        var scrollViewer = FindCurrentPageScrollViewer(window);
        scrollViewer.Offset = scrollViewer.Offset.WithY(y);
        return scrollViewer.Offset.Y;
    }

    private static ScrollViewer FindCurrentPageScrollViewer(MainWindow window)
    {
        return window.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .Where(IsControlEffectivelyVisible)
            .OrderByDescending(scrollViewer => scrollViewer.Bounds.Height)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Current page has no visible scroll container");
    }
}
#endif
