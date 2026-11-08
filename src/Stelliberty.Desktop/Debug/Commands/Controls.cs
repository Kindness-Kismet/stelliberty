#if DEBUG
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop.Debug;

internal static partial class DebugCommands
{
    private static string? ExecuteControlCommand(MainWindow window, string command)
    {
        if (string.Equals(command, "control.list", StringComparison.OrdinalIgnoreCase)
            || command.StartsWith("control.list --", StringComparison.OrdinalIgnoreCase))
        {
            return ReadWindowControls(window, command["control.list".Length..].Trim());
        }

        if (command.StartsWith("control.click ", StringComparison.OrdinalIgnoreCase))
        {
            ClickControl(window, command["control.click ".Length..].Trim());
            return null;
        }

        if (command.StartsWith("dropdown.open ", StringComparison.OrdinalIgnoreCase))
        {
            OpenDropDown(window, command["dropdown.open ".Length..].Trim());
            return null;
        }

        if (command.StartsWith("dropdown.select ", StringComparison.OrdinalIgnoreCase))
        {
            SelectComboBoxItem(window, command["dropdown.select ".Length..].Trim());
            return null;
        }

        if (command.StartsWith("control.input ", StringComparison.OrdinalIgnoreCase))
        {
            SetTextBoxText(window, command["control.input ".Length..].Trim());
            return null;
        }

        if (command.StartsWith("control.exists ", StringComparison.OrdinalIgnoreCase))
        {
            FindControlByAutomationId(window, command["control.exists ".Length..].Trim());
            return null;
        }

        if (command.StartsWith("control.get text ", StringComparison.OrdinalIgnoreCase))
        {
            return ReadControlText(window, command["control.get text ".Length..].Trim());
        }

        if (command.StartsWith("control.get pointer ", StringComparison.OrdinalIgnoreCase))
        {
            var control = FindControlByAutomationId(window, command["control.get pointer ".Length..].Trim());
            var center = control.PointToScreen(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2));
            return JsonSerializer.Serialize(new
            {
                WindowVisible = window.IsVisible,
                control.IsPointerOver,
                IsPressed = control is Button { IsPressed: true },
                Background = (control as Button)?.Background?.ToString(),
                X = center.X,
                Y = center.Y,
                WindowHandle = window.TryGetPlatformHandle()?.Handle.ToInt64(),
            });
        }

        if (command.StartsWith("dropdown.list items ", StringComparison.OrdinalIgnoreCase))
        {
            return ReadComboBoxItems(window, command["dropdown.list items ".Length..].Trim());
        }

        if (command.StartsWith("control.list nodes ", StringComparison.OrdinalIgnoreCase))
        {
            return ReadVisibleNodeNames(window, command["control.list nodes ".Length..].Trim());
        }

        if (command.StartsWith("control.list rules ", StringComparison.OrdinalIgnoreCase))
        {
            return ReadVisibleRulePayloads(window, command["control.list rules ".Length..].Trim());
        }

        if (command.StartsWith("control.list connections ", StringComparison.OrdinalIgnoreCase))
        {
            return ReadVisibleConnectionIds(window, command["control.list connections ".Length..].Trim());
        }

        if (command.StartsWith("control.list core-logs ", StringComparison.OrdinalIgnoreCase))
        {
            return ReadVisibleCoreLogPayloads(window, command["control.list core-logs ".Length..].Trim());
        }

        if (command.StartsWith("control.scroll y ", StringComparison.OrdinalIgnoreCase))
        {
            return ReadOrSetScrollViewerY(window, command["control.scroll y ".Length..].Trim()).ToString("0.###", CultureInfo.InvariantCulture);
        }

        throw new InvalidOperationException($"Unknown control command: {command}");
    }

    private static void ClickControl(MainWindow window, string automationId)
    {
        var control = FindControlByAutomationId(window, automationId);
        ActivateControl(window, control);

        if (control is RadioButton radioButton)
        {
            radioButton.IsChecked = true;
            return;
        }

        if (control is ToggleButton toggleButton)
        {
            if (toggleButton.Command?.CanExecute(toggleButton.CommandParameter) == true)
            {
                toggleButton.Command.Execute(toggleButton.CommandParameter);
                return;
            }

            toggleButton.IsChecked = !(toggleButton.IsChecked ?? false);
            return;
        }

        if (control is Button button)
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            if (button.Command?.CanExecute(button.CommandParameter) == true)
            {
                button.Command.Execute(button.CommandParameter);
            }
            return;
        }

        if (control is ComboBox comboBox)
        {
            comboBox.IsDropDownOpen = true;
            return;
        }

        if (control is TextBox)
        {
            return;
        }

        throw new InvalidOperationException($"Control does not support click: {automationId}");
    }

    private static void OpenDropDown(MainWindow window, string automationId)
    {
        if (FindControlByAutomationId(window, automationId) is not ComboBox comboBox)
        {
            throw new InvalidOperationException($"Not a dropdown: {automationId}");
        }

        ActivateControl(window, comboBox);
        comboBox.IsDropDownOpen = true;
    }

    private static void SelectComboBoxItem(MainWindow window, string spec)
    {
        var parts = spec.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var index))
        {
            throw new InvalidOperationException("dropdown.select usage: dropdown.select <automation_id> <index>");
        }

        if (FindControlByAutomationId(window, parts[0]) is not ComboBox comboBox)
        {
            throw new InvalidOperationException($"Not a dropdown: {parts[0]}");
        }

        if (index < 0 || index >= comboBox.Items.Count())
        {
            throw new InvalidOperationException($"Dropdown item index is out of range: {index}");
        }

        ActivateControl(window, comboBox);
        comboBox.SelectedIndex = index;
        comboBox.IsDropDownOpen = false;
    }

    private static void SetTextBoxText(MainWindow window, string spec)
    {
        var parts = spec.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            throw new InvalidOperationException("control.input usage: control.input <automation_id> <value>");
        }

        var control = FindControlByAutomationId(window, parts[0]);
        var text = parts[1] == "__EMPTY__" ? string.Empty : parts[1].Replace("\\n", Environment.NewLine);
        ActivateControl(window, control);

        if (control is TextBox textBox)
        {
            textBox.Text = text;
            return;
        }

        if (control is Stelliberty.Desktop.Controls.CodeEditor codeEditor)
        {
            codeEditor.Text = text;
            return;
        }

        if (control is ComboBox comboBox)
        {
            comboBox.Text = text;
            return;
        }

        throw new InvalidOperationException($"Not an input: {parts[0]}");
    }

    private static string ReadControlText(MainWindow window, string automationId)
    {
        var control = FindControlByAutomationId(window, automationId);
        return control switch
        {
            TextBlock textBlock => textBlock.Text ?? string.Empty,
            TextBox textBox => textBox.Text ?? string.Empty,
            Stelliberty.Desktop.Controls.CodeEditor codeEditor => codeEditor.Text ?? string.Empty,
            ContentControl contentControl => contentControl.Content?.ToString() ?? string.Empty,
            _ => throw new InvalidOperationException($"Control does not support text reading: {automationId}")
        };
    }

    private static string ReadWindowControls(MainWindow window, string spec)
    {
        var visibleOnly = string.Equals(spec, "--visible", StringComparison.OrdinalIgnoreCase);
        if (!visibleOnly && !string.IsNullOrWhiteSpace(spec))
        {
            throw new InvalidOperationException("control.list usage: control.list [--visible]");
        }

        var rows = window.GetVisualDescendants()
            .OfType<Control>()
            .Select(control => new
            {
                Id = AutomationProperties.GetAutomationId(control),
                Type = control.GetType().Name,
                Visible = IsControlEffectivelyVisible(control)
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .Where(item => !visibleOnly || item.Visible)
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .Select(item => $"{item.Id}\t{item.Type}\tvisible={item.Visible.ToString().ToLowerInvariant()}");
        return string.Join("\n", rows);
    }

    private static string ReadComboBoxItems(MainWindow window, string automationId)
    {
        if (FindControlByAutomationId(window, automationId) is not ComboBox comboBox)
        {
            throw new InvalidOperationException($"Not a dropdown: {automationId}");
        }

        var items = comboBox.Items
            .Select(item => item switch
            {
                SubscriptionRowMenuSelection selection => selection.DisplayName,
                ChainProxyGroupOption group => group.Name,
                _ => item?.ToString() ?? string.Empty
            })
            .Where(text => !string.IsNullOrWhiteSpace(text));
        return string.Join("|", items);
    }

    private static bool IsControlEffectivelyVisible(Control control)
    {
        for (var current = control; current is not null; current = current.GetVisualParent<Control>())
        {
            if (!current.IsVisible || current.Opacity <= 0.001)
            {
                return false;
            }
        }

        return true;
    }

    private static string ReadVisibleNodeNames(MainWindow window, string automationId)
    {
        var control = FindControlByAutomationId(window, automationId);
        var names = control.GetVisualDescendants()
            .OfType<Border>()
            .Where(border => border.Classes.Contains("node"))
            .Select(border => border.Tag as string)
            .Where(name => !string.IsNullOrEmpty(name));
        return string.Join("|", names);
    }

    private static string ReadVisibleConnectionIds(MainWindow window, string automationId)
    {
        var control = FindControlByAutomationId(window, automationId);
        var ids = control.GetVisualDescendants()
            .OfType<Control>()
            .Select(AutomationProperties.GetAutomationId)
            .Where(id => id?.StartsWith("Connections.Row.", StringComparison.Ordinal) == true)
            .Select(id => id!["Connections.Row.".Length..])
            .Where(id => !id.Contains('.'));
        return string.Join("|", ids);
    }

    private static string ReadVisibleRulePayloads(MainWindow window, string automationId)
    {
        var control = FindControlByAutomationId(window, automationId);
        var payloads = control.GetVisualDescendants()
            .OfType<Border>()
            .Select(border => border.Tag as string)
            .Where(payload => !string.IsNullOrEmpty(payload));
        return string.Join("|", payloads);
    }

    private static string ReadVisibleCoreLogPayloads(MainWindow window, string automationId)
    {
        var control = FindControlByAutomationId(window, automationId);
        var payloads = control.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(textBlock => AutomationProperties.GetAutomationId(textBlock)?.EndsWith(".PayloadText", StringComparison.Ordinal) == true)
            .Select(textBlock => textBlock.Text)
            .Where(payload => !string.IsNullOrEmpty(payload));
        return string.Join("|", payloads);
    }

    private static double ReadOrSetScrollViewerY(MainWindow window, string spec)
    {
        var parts = spec.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            return ReadScrollViewerY(window, parts[0]);
        }

        if (parts.Length != 2 || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            throw new InvalidOperationException("control.scroll y usage: control.scroll y <automation_id> [y]");
        }

        var scrollViewer = FindScrollViewer(window, parts[0]);
        scrollViewer.Offset = scrollViewer.Offset.WithY(y);
        return ReadScrollViewerY(window, parts[0]);
    }

    private static double ReadScrollViewerY(MainWindow window, string automationId)
    {
        return FindScrollViewer(window, automationId).Offset.Y;
    }

    private static ScrollViewer FindScrollViewer(MainWindow window, string automationId)
    {
        var control = FindControlByAutomationId(window, automationId);
        return control as ScrollViewer
            ?? control.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()
            ?? throw new InvalidOperationException($"Control has no scroll container: {automationId}");
    }
}
#endif
