#if DEBUG
using System.Globalization;
using System.Text;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Subscriptions;
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop.Debug;

internal static partial class DebugCommands
{
    private static void ActivateControl(MainWindow window, Control control)
    {
        if (!window.DebugFocus.IsNoFocus) window.Activate();
        control.Focus();
    }

    private static Control FindControlByAutomationId(MainWindow window, string automationId)
    {
        if (string.IsNullOrWhiteSpace(automationId))
        {
            throw new InvalidOperationException("AutomationId is empty");
        }

        var descendants = window.GetVisualDescendants().OfType<Control>();
        var control = descendants.FirstOrDefault(item => string.Equals(
            AutomationProperties.GetAutomationId(item),
            automationId,
            StringComparison.Ordinal));
        return control ?? throw new InvalidOperationException($"Control not found: {automationId}");
    }

    private static MainWindowViewModel RequireViewModel(MainWindow window)
    {
        return window.DataContext as MainWindowViewModel
            ?? throw new InvalidOperationException("DataContext is not ready");
    }

    private static ISubscriptionStore GetSubscriptionStore(MainWindow window)
    {
        return RequireViewModel(window).SubscriptionPage.SubscriptionStore
            ?? throw new InvalidOperationException("SubscriptionStore is not injected");
    }

    private static ISubscriptionSelectionStore GetSelectionStore(MainWindow window)
    {
        return RequireViewModel(window).SubscriptionPage.SelectionStore
            ?? throw new InvalidOperationException("SelectionStore is not injected");
    }

    private static List<string> SplitCommandTokens(string spec)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        char? quote = null;
        var escaped = false;
        var hasToken = false;
        foreach (var character in spec)
        {
            if (escaped)
            {
                current.Append(character);
                escaped = false;
                hasToken = true;
                continue;
            }

            if (character == '\\')
            {
                escaped = true;
                continue;
            }

            if (quote is not null)
            {
                if (character == quote)
                {
                    quote = null;
                }
                else
                {
                    current.Append(character);
                }
                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
                hasToken = true;
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (hasToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }
                continue;
            }

            current.Append(character);
            hasToken = true;
        }

        if (escaped)
        {
            current.Append('\\');
            hasToken = true;
        }

        if (quote is not null)
        {
            throw new InvalidOperationException("Command argument quote is not closed");
        }

        if (hasToken)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    private static string? ExtractFlag(List<string> tokens, string flag)
    {
        var index = tokens.FindIndex(token => string.Equals(token, flag, StringComparison.Ordinal));
        return index >= 0 && index + 1 < tokens.Count ? tokens[index + 1] : null;
    }

    private static int ParseInt(string? value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;
    }

    // cli 对含空格参数做 quote_arg 包裹，取值必须走 token 解析而非裸 Trim。
    private static string FirstCommandToken(string spec)
        => SplitCommandTokens(spec.Trim()).FirstOrDefault() ?? string.Empty;

    private static string NormalizeInputValue(string value)
    {
        return value == "__EMPTY__" ? string.Empty : value.Replace("\\n", Environment.NewLine);
    }

    private static string OutputValue(string value)
    {
        return value.Replace("\r\n", "\n").Replace("\n", "\\n");
    }

    private static string NormalizeListInput(string value)
    {
        return NormalizeInputValue(value).Replace("|", Environment.NewLine);
    }

    private static string ListValue(string value)
    {
        return value.Replace(Environment.NewLine, "|");
    }

    private static string Bool(bool value)
    {
        return value.ToString().ToLowerInvariant();
    }

    private static bool ParseBool(string value)
    {
        return value switch
        {
            "true" => true,
            "false" => false,
            _ => throw new InvalidOperationException($"Invalid boolean value: {value}. Expected true or false.")
        };
    }
}
#endif
