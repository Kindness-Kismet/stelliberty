using Avalonia.Controls;
using Stelliberty.Domain.Proxies;
using Stelliberty.Presentation.Proxies;

namespace Stelliberty.Tray;

internal sealed class TrayProxyMenu
{
    private readonly Func<string, string> _localize;
    private readonly Func<string, string, string, Task> _select;
    private readonly Dictionary<string, GroupMenu> _groups = new(StringComparer.Ordinal);
    private TrayProxySnapshot _snapshot = new(string.Empty, new ProxyConfig([], new Dictionary<string, ProxyNode>()), false);

    public TrayProxyMenu(Func<string, string> localize, Func<string, string, string, Task> select)
    {
        _localize = localize;
        _select = select;
        Item = new NativeMenuItem { Header = _localize("Tray.ProxyGroups"), Menu = new NativeMenu() };
        Item.Menu.NeedsUpdate += (_, _) => UpdateGroups();
        UpdateGroups();
    }

    public NativeMenuItem Item { get; }

    public void Update(TrayProxySnapshot snapshot)
    {
        _snapshot = snapshot;
        Item.Header = _localize("Tray.ProxyGroups");
        Item.IsEnabled = snapshot.IsCoreRunning && snapshot.Config.VisibleGroups.Count > 0;
        if (OperatingSystem.IsWindows())
        {
            // Windows 托管托盘菜单不会触发 NeedsUpdate，菜单内容必须随后台快照更新。
            UpdateGroups();
            foreach (var (name, group) in _groups) UpdateNodes(name, group);
        }
    }

    private void UpdateGroups()
    {
        var visible = _snapshot.IsCoreRunning ? _snapshot.Config.VisibleGroups : [];
        var names = visible.Select(group => group.Name).ToArray();
        var menu = Item.Menu!;
        Item.IsEnabled = names.Length > 0;
        if (!_groups.Keys.SequenceEqual(names, StringComparer.Ordinal))
        {
            menu.Items.Clear();
            var retained = new Dictionary<string, GroupMenu>(StringComparer.Ordinal);
            foreach (var name in names)
            {
                var group = _groups.TryGetValue(name, out var existing) ? existing : CreateGroup(name);
                retained.Add(name, group);
                menu.Items.Add(group.Item);
            }
            _groups.Clear();
            foreach (var entry in retained) _groups.Add(entry.Key, entry.Value);
        }

        foreach (var group in visible)
        {
            var item = _groups[group.Name].Item;
            item.Header = string.IsNullOrWhiteSpace(group.DisplaySelectionName)
                ? group.Name : $"{group.Name} · {group.DisplaySelectionName}";
            item.ToolTip = item.Header;
        }
    }

    private GroupMenu CreateGroup(string name)
    {
        var group = new GroupMenu(new TrayProxyGroupMenuItem(name) { Menu = new NativeMenu() });
        // 原生导出端在展开分组时更新节点，托管菜单由快照驱动。
        group.Item.Menu!.NeedsUpdate += (_, _) => UpdateNodes(name, group);
        return group;
    }

    private void UpdateNodes(string groupName, GroupMenu menu)
    {
        var group = _snapshot.Config.VisibleGroups.FirstOrDefault(item => item.Name == groupName);
        var names = group?.All.Distinct(StringComparer.Ordinal).ToArray() ?? [];
        if (!menu.Nodes.Keys.SequenceEqual(names, StringComparer.Ordinal))
        {
            menu.Item.Menu!.Items.Clear();
            menu.Nodes.Clear();
            foreach (var name in names)
            {
                var node = new TrayProxyNodeMenuItem(groupName, name) { ToggleType = MenuItemToggleType.Radio, ToolTip = name };
                node.Click += (_, _) => _ = _select(menu.Scope, groupName, name);
                menu.Nodes.Add(name, node);
                menu.Item.Menu.Items.Add(node);
            }
        }

        menu.Scope = _snapshot.Scope;
        foreach (var (name, node) in menu.Nodes)
        {
            _snapshot.Config.TryGetEntryDelay(name, out var delay);
            node.DelayText = delay is null ? "-" : $"{delay} ms";
            node.DelayLevel = ProxyDelayPresentation.GetLevel(delay);
            node.Header = $"{name} · {node.DelayText}";
            node.IsChecked = name == group?.DisplaySelectionName;
            node.IsEnabled = _snapshot.IsCoreRunning && group?.IsManualSelectable == true
                && ProxyConfigSelectionNormalizer.HasEntry(_snapshot.Config, name);
        }
    }

#if DEBUG
    public Dictionary<NativeMenuItem, string> GetAutomationIds()
    {
        var ids = new Dictionary<NativeMenuItem, string> { [Item] = "Tray.ProxyGroups" };
        foreach (var (name, group) in _groups)
        {
            ids.Add(group.Item, group.Item.AutomationId);
            foreach (var node in group.Nodes.Values)
            {
                ids.Add(node, node.AutomationId);
            }
        }
        return ids;
    }

    public Task SelectAsync(string groupName, string nodeName)
    {
        var group = _groups[groupName];
        UpdateNodes(groupName, group);
        if (!group.Nodes[nodeName].IsEnabled)
        {
            throw new InvalidOperationException("Proxy node is not selectable.");
        }
        return _select(group.Scope, groupName, nodeName);
    }

    public object Inspect(bool isBelowShowWindow)
    {
        if (!OperatingSystem.IsWindows())
        {
            UpdateGroups();
            foreach (var (name, group) in _groups) UpdateNodes(name, group);
        }
        return new
        {
            // 原生菜单不属于 Avalonia 控件，调试定位使用稳定语义标识。
            AutomationId = "Tray.ProxyGroups",
            IsBelowShowWindow = isBelowShowWindow,
            HasSubmenu = Item.Menu!.Items.Count > 0,
            Item.IsEnabled,
            _snapshot.Scope,
            _snapshot.IsCoreRunning,
            Groups = _groups.Select(pair => new
            {
                Name = pair.Key,
                pair.Value.Item.AutomationId,
                pair.Value.Item.Header,
                HasSubmenu = pair.Value.Item.Menu!.Items.Count > 0,
                Nodes = pair.Value.Nodes.Select(node => new
                {
                    Name = node.Key,
                    node.Value.AutomationId,
                    node.Value.Header,
                    node.Value.DelayText,
                    node.Value.IsChecked,
                    node.Value.IsEnabled,
                }).ToArray(),
            }).ToArray(),
        };
    }
#endif

    private sealed class GroupMenu(TrayProxyGroupMenuItem item)
    {
        public TrayProxyGroupMenuItem Item { get; } = item;
        public string Scope { get; set; } = string.Empty;
        public Dictionary<string, TrayProxyNodeMenuItem> Nodes { get; } = new(StringComparer.Ordinal);
    }
}
