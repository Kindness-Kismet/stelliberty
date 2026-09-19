#if DEBUG
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop.Debug;

internal static partial class DebugCommands
{
    private static async Task<string?> ExecuteProxiesCommandAsync(MainWindow window, string command)
    {
        var viewModel = RequireViewModel(window);
        var page = viewModel.ProxyPage;
        // 非当前页会释放行缓存，调试命令读取前按现有展示生命周期重建。
        page.WarmupPresentation();
        var spec = command["proxies.".Length..].Trim();
        if (string.Equals(spec, "list groups", StringComparison.OrdinalIgnoreCase))
        {
            return string.Join("|", page.VisibleGroupRows.Select(row =>
                $"{row.Name}\t{row.Group.Type}\tnow={row.Group.Now ?? string.Empty}\tfixed={row.Group.Fixed ?? string.Empty}\tselectable={row.Group.IsManualSelectable.ToString().ToLowerInvariant()}\tselected={row.IsSelected.ToString().ToLowerInvariant()}"));
        }

        if (spec.StartsWith("list nodes", StringComparison.OrdinalIgnoreCase))
        {
            var groupName = FirstCommandToken(spec["list nodes".Length..]);
            if (!string.IsNullOrWhiteSpace(groupName))
            {
                page.SelectGroup(groupName);
            }

            return string.Join("|", page.VisibleNodeRows.Select(row =>
                ProxyDelayRow(row)));
        }

        if (spec.StartsWith("get delay ", StringComparison.OrdinalIgnoreCase))
        {
            var name = FirstCommandToken(spec["get delay ".Length..]);
            var row = page.VisibleNodeRows.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
            if (row is null)
            {
                throw new InvalidOperationException($"Proxy row not found in current group: {name}");
            }

            return ProxyDelayRow(row);
        }

        if (spec.StartsWith("select group ", StringComparison.OrdinalIgnoreCase))
        {
            page.SelectGroup(FirstCommandToken(spec["select group ".Length..]));
            return ProxyState(page);
        }

        if (spec.StartsWith("select node ", StringComparison.OrdinalIgnoreCase))
        {
            await page.SelectNodeAsync(FirstCommandToken(spec["select node ".Length..]));
            return ProxyState(page);
        }

        if (spec.StartsWith("test node ", StringComparison.OrdinalIgnoreCase))
        {
            page.TestNodeDelay(FirstCommandToken(spec["test node ".Length..]));
            return ProxyState(page);
        }

        if (spec.StartsWith("test group ", StringComparison.OrdinalIgnoreCase))
        {
            page.TestGroupDelays(FirstCommandToken(spec["test group ".Length..]));
            return ProxyState(page);
        }

        if (string.Equals(spec, "test current-group", StringComparison.OrdinalIgnoreCase))
        {
            page.TestCurrentGroupDelays();
            return ProxyState(page);
        }

        if (string.Equals(spec, "test all", StringComparison.OrdinalIgnoreCase))
        {
            page.TestAllDelays();
            return ProxyState(page);
        }

        if (string.Equals(spec, "refresh", StringComparison.OrdinalIgnoreCase))
        {
            await page.RefreshProxiesAsync();
            return ProxyState(page);
        }

        if (string.Equals(spec, "cancel delay", StringComparison.OrdinalIgnoreCase))
        {
            page.CancelDelayTests();
            return ProxyState(page);
        }

        if (spec.StartsWith("set layout ", StringComparison.OrdinalIgnoreCase))
        {
            var mode = spec["set layout ".Length..].Trim();
            if (!Enum.TryParse<ProxyPageLayout>(mode, ignoreCase: true, out var layout))
            {
                throw new InvalidOperationException($"Unknown layout: {mode}");
            }

            page.SetLayout(layout);
            return ProxyState(page);
        }

        if (string.Equals(spec, "toggle layout", StringComparison.OrdinalIgnoreCase))
        {
            page.ToggleLayout();
            return ProxyState(page);
        }

        if (spec.StartsWith("expand group ", StringComparison.OrdinalIgnoreCase))
        {
            var name = FirstCommandToken(spec["expand group ".Length..]);
            if (!string.Equals(page.ExpandedGroupName, name, StringComparison.Ordinal))
            {
                page.ToggleGroupExpand(name);
            }

            return ProxyState(page);
        }

        if (string.Equals(spec, "collapse group", StringComparison.OrdinalIgnoreCase))
        {
            if (page.ExpandedGroupName is { } expanded)
            {
                page.ToggleGroupExpand(expanded);
            }

            return ProxyState(page);
        }

        if (string.Equals(spec, "state", StringComparison.OrdinalIgnoreCase))
        {
            return ProxyState(page);
        }

        throw new InvalidOperationException($"Unknown proxies command: {command}");
    }

    private static string ProxyDelayRow(ProxyNodeRowViewModel row)
    {
        return $"{row.Name}\t{row.Type}\tdelay={row.DelayText}\tstate={row.DelayState}\tlevel={row.DelayLevel}\tselected={row.IsSelected.ToString().ToLowerInvariant()}\tclickable={row.IsClickable.ToString().ToLowerInvariant()}";
    }

    private static string ProxyState(ProxyPageViewModel page)
    {
        var testedNodeNames = page.DelayTestedNodeNames
            .Concat(page.BatchDelayTestedNodeNames)
            .Distinct(StringComparer.Ordinal);
        return string.Join(";", [
            $"groups={page.VisibleGroups.Count}",
            $"selected={page.SelectedGroup?.Name ?? string.Empty}",
            $"now={page.SelectedGroup?.Now ?? string.Empty}",
            $"fixed={page.SelectedGroup?.Fixed ?? string.Empty}",
            $"nodes={page.VisibleNodeRows.Count}",
            $"last={page.LastSelectedNodeName ?? string.Empty}",
            $"testing={page.IsDelayTesting.ToString().ToLowerInvariant()}",
            $"batch={page.IsBatchDelayTesting.ToString().ToLowerInvariant()}",
            $"tested={string.Join(',', testedNodeNames)}",
            $"layout={page.LayoutMode.ToString().ToLowerInvariant()}",
            $"sort={page.SortMode.ToString().ToLowerInvariant()}",
            $"expanded={page.ExpandedGroupName ?? string.Empty}"
        ]);
    }
}
#endif
