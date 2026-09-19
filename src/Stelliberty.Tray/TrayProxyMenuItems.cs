using Avalonia;
using Avalonia.Controls;
using Stelliberty.Presentation.Proxies;

namespace Stelliberty.Tray;

internal sealed class TrayProxyGroupMenuItem(string name) : NativeMenuItem
{
    public string AutomationId { get; } = $"Tray.ProxyGroup.{Uri.EscapeDataString(name)}";
    public string NameAutomationId => $"{AutomationId}.Name";
}

internal sealed class TrayProxyNodeMenuItem(string groupName, string name) : NativeMenuItem
{
    public static readonly StyledProperty<string> DelayTextProperty =
        AvaloniaProperty.Register<TrayProxyNodeMenuItem, string>(nameof(DelayText), "-");
    public static readonly StyledProperty<string> DelayLevelProperty =
        AvaloniaProperty.Register<TrayProxyNodeMenuItem, string>(nameof(DelayLevel), ProxyDelayPresentation.GetLevel(null));

    public string Name { get; } = name;
    public string AutomationId { get; } = $"Tray.ProxyNode.{Uri.EscapeDataString(groupName)}.{Uri.EscapeDataString(name)}";
    public string NameAutomationId => $"{AutomationId}.Name";
    public string DelayAutomationId => $"{AutomationId}.Delay";

    public string DelayText
    {
        get => GetValue(DelayTextProperty);
        set => SetValue(DelayTextProperty, value);
    }

    public string DelayLevel
    {
        get => GetValue(DelayLevelProperty);
        set => SetValue(DelayLevelProperty, value);
    }
}
