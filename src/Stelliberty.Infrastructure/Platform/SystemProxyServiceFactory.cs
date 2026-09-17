using Stelliberty.Application.Platform;

namespace Stelliberty.Infrastructure.Platform;

public static class SystemProxyServiceFactory
{
    public static ISystemProxyService Create(SystemProxyPlatform platform, string appDataDirectory)
    {
        return platform switch
        {
            SystemProxyPlatform.Windows => new WindowsSystemProxyService(appDataDirectory),
            SystemProxyPlatform.MacOS => new MacOSSystemProxyService(appDataDirectory),
            SystemProxyPlatform.Linux => new LinuxSystemProxyService(appDataDirectory),
            SystemProxyPlatform.Other => new UnsupportedSystemProxyService(),
            _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unknown system proxy platform")
        };
    }
}
