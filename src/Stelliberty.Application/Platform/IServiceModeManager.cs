using Stelliberty.Application.Runtime;

namespace Stelliberty.Application.Platform;

public interface IServiceModeManager
{
    // 安装态与版本：只在启动和用户操作时查，代价高。
    Task<ServiceModeStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<ServiceModeOperationResult> InstallOrUpdateAsync(CancellationToken cancellationToken = default);

    Task<ServiceModeOperationResult> UninstallAsync(CancellationToken cancellationToken = default);

}
