using System.Text;
using Stelliberty.Application.Diagnostics;

namespace Stelliberty.Infrastructure.Diagnostics;

public sealed class FileAppLogExporter(string logFilePath, params string[] additionalLogFilePaths) : IAppLogExporter
{
    public async Task ExportAsync(string exportPath, CancellationToken cancellationToken = default)
    {
        var fullExportPath = Path.GetFullPath(exportPath);
        var logFilePaths = additionalLogFilePaths.Prepend(logFilePath);
        // 尚未创建的服务日志也保留为源路径，禁止导出文件占用它们。
        if (logFilePaths.Any(path => string.Equals(Path.GetFullPath(path), fullExportPath, StringComparison.OrdinalIgnoreCase)))
        {
            throw new IOException("The export path must be different from the source log paths.");
        }

        await using var source = OpenLog(logFilePath);
        await using var target = new FileStream(
            exportPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);
        await CopyLogAsync(source, target, cancellationToken).ConfigureAwait(false);
        foreach (var additionalLogFilePath in additionalLogFilePaths)
        {
            await using var additionalSource = OpenOptionalLog(additionalLogFilePath);
            if (additionalSource is not null)
            {
                await CopyLogAsync(additionalSource, target, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static FileStream OpenLog(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete,
        bufferSize: 81920,
        useAsync: true);

    private static FileStream? OpenOptionalLog(string path)
    {
        try
        {
            return OpenLog(path);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            // 未安装服务或日志正在轮转时，缺少附加文件不影响主日志导出。
            return null;
        }
    }

    private static async Task CopyLogAsync(FileStream source, Stream target, CancellationToken cancellationToken)
    {
        var header = Encoding.UTF8.GetBytes($"===== {Path.GetFileName(source.Name)} ====={Environment.NewLine}");
        await target.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        await target.WriteAsync(Encoding.UTF8.GetBytes(Environment.NewLine), cancellationToken).ConfigureAwait(false);
    }
}
