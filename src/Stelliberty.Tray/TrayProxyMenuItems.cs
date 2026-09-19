using Avalonia;
using Avalonia.Controls;
using Stelliberty.Presentation.Proxies;

namespace Stelliberty.Tray;

internal sealed class TrayProxyGroupMenuItem(string name) : NativeMenuItem
{
    public string AutomationId { get; } = $"Tray.ProxyGroup.{TrayProxyAutomationId.Encode(name)}";
    public string NameAutomationId => $"{AutomationId}.Name";
}

internal sealed class TrayProxyNodeMenuItem(string groupName, string name) : NativeMenuItem
{
    public static readonly StyledProperty<string> DelayTextProperty =
        AvaloniaProperty.Register<TrayProxyNodeMenuItem, string>(nameof(DelayText), "-");
    public static readonly StyledProperty<string> DelayLevelProperty =
        AvaloniaProperty.Register<TrayProxyNodeMenuItem, string>(nameof(DelayLevel), ProxyDelayPresentation.GetLevel(null));

    public string Name { get; } = name;
    public string AutomationId { get; } = $"Tray.ProxyNode.{TrayProxyAutomationId.Encode(groupName)}.{TrayProxyAutomationId.Encode(name)}";
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

internal static class TrayProxyAutomationId
{
    // 句点用于分隔名称和控件后缀，名称中的句点必须另行转义。
    public static string Encode(string name) => Uri.EscapeDataString(name).Replace(".", "%2E", StringComparison.Ordinal);
}
