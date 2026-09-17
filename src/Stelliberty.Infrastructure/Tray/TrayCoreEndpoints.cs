using Stelliberty.Application.Platform;

namespace Stelliberty.Infrastructure.Tray;

public static class TrayCoreEndpoints
{
#if DEBUG
    public static readonly string Hub = BuildHubEndpoint(AppMetadata.PipePrefix + "_core_dev");
    public static readonly string Core = BuildCoreEndpoint(AppMetadata.PipePrefix + "_mihomo_dev");
#else
    public static readonly string Hub = BuildHubEndpoint(AppMetadata.PipePrefix + "_core_prod");
    public static readonly string Core = BuildCoreEndpoint(AppMetadata.PipePrefix + "_mihomo_prod");
#endif

    private static string BuildCoreEndpoint(string name)
    {
        return OperatingSystem.IsWindows()
            ? $@"\\.\pipe\{name}"
            : Path.Combine(Path.GetTempPath(), $"{name}.sock");
    }

    private static string BuildHubEndpoint(string name)
    {
        return OperatingSystem.IsWindows()
            ? name
            : Path.Combine(Path.GetTempPath(), $"{name}.sock");
    }
}
