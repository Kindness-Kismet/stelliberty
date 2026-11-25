#if DEBUG
using Avalonia.Input;

namespace Stelliberty.Desktop.Debug;

internal static partial class DebugCommands
{
    private static string? ExecuteKeyboardCommand(MainWindow window, string command)
    {
        if (command.StartsWith("keyboard.press ", StringComparison.OrdinalIgnoreCase))
        {
            PressKeyboardShortcut(window, command["keyboard.press ".Length..].Trim());
            return null;
        }

        throw new InvalidOperationException($"Unknown keyboard command: {command}");
    }

    private static void PressKeyboardShortcut(MainWindow window, string spec)
    {
        var tokens = spec.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            throw new InvalidOperationException("Keyboard shortcut is empty");
        }

        var modifiers = KeyModifiers.None;
        for (var index = 0; index < tokens.Length - 1; index++)
        {
            modifiers |= ParseKeyModifier(tokens[index]);
        }

        var key = ParseKey(tokens[^1]);
        var target = window.FocusManager?.GetFocusedElement() as InputElement ?? window;
        if (!window.DebugFocus.IsNoFocus) window.Activate();
        target.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Source = target,
            Key = key,
            KeyModifiers = modifiers,
        });
        target.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyUpEvent,
            Source = target,
            Key = key,
            KeyModifiers = modifiers,
        });
    }

    private static KeyModifiers ParseKeyModifier(string token)
    {
        return token.ToLowerInvariant() switch
        {
            "ctrl" or "control" => KeyModifiers.Control,
            "shift" => KeyModifiers.Shift,
            "alt" => KeyModifiers.Alt,
            "meta" or "win" or "windows" or "cmd" or "command" => KeyModifiers.Meta,
            _ => throw new InvalidOperationException($"Unknown keyboard modifier: {token}"),
        };
    }

    private static Key ParseKey(string token)
    {
        if (token.Length == 1 && token[0] is >= '0' and <= '9')
        {
            return (Key)((int)Key.D0 + token[0] - '0');
        }

        return token.ToLowerInvariant() switch
        {
            "esc" => Key.Escape,
            "left" => Key.Left,
            "right" => Key.Right,
            "up" => Key.Up,
            "down" => Key.Down,
            "space" => Key.Space,
            "return" => Key.Enter,
            _ when Enum.TryParse<Key>(token, ignoreCase: true, out var key) => key,
            _ => throw new InvalidOperationException($"Unknown keyboard key: {token}"),
        };
    }
}
#endif
