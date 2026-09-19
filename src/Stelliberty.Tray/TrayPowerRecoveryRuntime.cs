using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Runtime;
using Stelliberty.Infrastructure.Platform;
using Stelliberty.Infrastructure.Settings;

namespace Stelliberty.Tray;

internal sealed class TrayPowerRecoveryRuntime(
    TrayCoreRuntimeHost coreRuntime,
    LocalSystemProxyController systemProxy,
    SystemProxyPlatform platform) : ICorePowerRecovery
{
    public CorePowerRecoveryContext CaptureContext() => coreRuntime.CapturePowerContext();

    public async Task<CorePowerRecoveryResult> RecoverAsync(
        CorePowerRecoveryContext context, CancellationToken cancellationToken)
    {
        var result = await coreRuntime.RecoverCoreAfterResumeAsync(context, cancellationToken).ConfigureAwait(false);
        if (result.Outcome != CorePowerRecoveryOutcome.Recovered) return result;
        if (!coreRuntime.IsPowerRecoveryCurrent(context))
        {
            return new(CorePowerRecoveryOutcome.Skipped, "Core operation changed before system proxy recovery.");
        }

        var settings = new JsonAppSettingsStore(new TrayPlatformDirectories()).Load();
        var proxy = await systemProxy.ReapplyOwnedAsync(
            SystemProxyApplicationRequest.Build(settings, platform), cancellationToken).ConfigureAwait(false);
        AppLogger.Info($"Power system proxy recovery: enabled={proxy.Status.IsEnabled} owned={proxy.Status.IsOwned} success={proxy.IsSuccess} detail={proxy.Message}");
        return proxy.IsSuccess ? result : new(CorePowerRecoveryOutcome.Retry, $"System proxy recovery failed: {proxy.Message}");
    }
}
