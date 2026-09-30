namespace Stelliberty.Infrastructure.Core;

public sealed record CoreAssetDeployResult(IReadOnlyList<string> DeployedAssets, IReadOnlyList<string> Failures);

public static class CoreAssetDeployer
{
    // 必须与 scripts/build_support/builder.py 的 CORE_ASSET_NAMES 一致。
    private static readonly string[] GeoAssetNames = ["asn.mmdb", "country.mmdb", "geoip.dat", "geoip.metadb", "geosite.dat"];

    // 安装载体只读：核心与 Geo 资源复制到可写核心目录，修改时间新者优先，保留核心更新与 Geo 自动更新的结果。
    public static CoreAssetDeployResult Deploy(string installCoreDirectory, string coreDirectory, string coreBinaryName)
    {
        if (IsSameDirectory(installCoreDirectory, coreDirectory))
        {
            return new CoreAssetDeployResult([], []);
        }

        Directory.CreateDirectory(coreDirectory);
        var deployedAssets = new List<string>();
        var failures = new List<string>();
        string[] assetNames = [coreBinaryName, .. GeoAssetNames];
        foreach (var assetName in assetNames)
        {
            var source = Path.Combine(installCoreDirectory, assetName);
            if (!File.Exists(source))
            {
                continue;
            }

            var target = Path.Combine(coreDirectory, assetName);
            var sourceWriteTime = File.GetLastWriteTimeUtc(source);
            if (File.Exists(target) && File.GetLastWriteTimeUtc(target) >= sourceWriteTime)
            {
                continue;
            }

            try
            {
                CopyAtomically(source, target, sourceWriteTime, isExecutable: assetName == coreBinaryName);
                deployedAssets.Add(assetName);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failures.Add($"{assetName}: {exception.Message}");
            }
        }

        return new CoreAssetDeployResult(deployedAssets, failures);
    }

    private static void CopyAtomically(string source, string target, DateTime sourceWriteTime, bool isExecutable)
    {
        var temporaryPath = target + ".deploying";
        // 流式复制只写内容，不继承安装载体上的扩展属性。
        using (var input = File.OpenRead(source))
        using (var output = File.Create(temporaryPath))
        {
            input.CopyTo(output);
        }

        if (isExecutable && !OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                temporaryPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        File.SetLastWriteTimeUtc(temporaryPath, sourceWriteTime);
        // 核心可能正在运行，只能改名替换，不能原地覆写。
        File.Move(temporaryPath, target, overwrite: true);
    }

    private static bool IsSameDirectory(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
        StringComparison.OrdinalIgnoreCase);
}
