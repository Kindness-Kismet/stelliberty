using Stelliberty.Application.Diagnostics;

namespace Stelliberty.Infrastructure.Diagnostics;

public sealed class FileAppLogExporter(string logFilePath) : IAppLogExporter
{
    public async Task ExportAsync(string exportPath, CancellationToken cancellationToken = default)
    {
        var fullExportPath = Path.GetFullPath(exportPath);
        // 禁止将导出目标指向运行日志，避免清空仍在写入的诊断信息。
        if (string.Equals(Path.GetFullPath(logFilePath), fullExportPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("The export path must be different from the running log path.");
        }

        await using var source = new FileStream(
            logFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 81920,
            useAsync: true);
        await using var target = new FileStream(
            exportPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);
        await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
    }
}
