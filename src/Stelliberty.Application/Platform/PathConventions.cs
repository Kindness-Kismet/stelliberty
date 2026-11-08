namespace Stelliberty.Application.Platform;

public static class PathConventions
{
    public const string DataDirectoryName = "data";
    public const string DepsSubdirectory = "deps";
    public const string CoreSubdirectory = "core";

    public const string ServiceSubdirectory = "service";
    public const string ServiceUpdateSubdirectory = "update";
    public static string ServiceInstalledBinaryStem => AppRuntimeNames.ServiceInstalledBinaryStem;
    public const string RuntimeSubdirectory = "runtime";
    public const string AppLogsSubdirectory = "applogs";
    public const string SettingsFileName = "settings.json";
    public const string RunningLogFileName = "running.logs";
}
