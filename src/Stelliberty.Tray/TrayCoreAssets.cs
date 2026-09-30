using Stelliberty.Application.Diagnostics;
using Stelliberty.Infrastructure.Core;

namespace Stelliberty.Tray;

internal static class TrayCoreAssets
{
    // 须在启动核心前完成；Windows 便携安装时核心目录即安装目录，部署为空操作。
    public static void Deploy()
    {
        var result = CoreAssetDeployer.Deploy(
            TrayApplicationLayout.InstallCoreDirectory,
            TrayApplicationLayout.CoreDirectory,
            Path.GetFileName(TrayApplicationLayout.CoreBinaryPath));
        if (result.DeployedAssets.Count > 0)
        {
            AppLogger.Info($"Core assets deployed to {TrayApplicationLayout.CoreDirectory}: {string.Join(", ", result.DeployedAssets)}");
        }

        if (result.Failures.Count > 0)
        {
            AppLogger.Warning($"Core asset deployment failed: {string.Join("; ", result.Failures)}");
        }
    }
}
