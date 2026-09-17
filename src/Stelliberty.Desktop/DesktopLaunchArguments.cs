namespace Stelliberty.Desktop;

internal sealed record DesktopLaunchArguments(
    string TraySessionToken,
    string[] AvaloniaArguments)
{
    private const string UiArgument = "--ui";
    private const string TraySessionArgument = "--tray-session";

    public static DesktopLaunchArguments? Parse(string[] args)
    {
        var uiIndex = Array.IndexOf(args, UiArgument);
        if (uiIndex < 0)
        {
            return null;
        }

        var sessionIndex = Array.IndexOf(args, TraySessionArgument);
        if (sessionIndex < 0 || sessionIndex + 1 >= args.Length || string.IsNullOrWhiteSpace(args[sessionIndex + 1]))
        {
            return null;
        }

        var internalIndexes = new HashSet<int> { uiIndex, sessionIndex, sessionIndex + 1 };
        var avaloniaArguments = args
            .Where((_, index) => !internalIndexes.Contains(index))
            .ToArray();
        return new DesktopLaunchArguments(
            args[sessionIndex + 1],
            avaloniaArguments);
    }
}
