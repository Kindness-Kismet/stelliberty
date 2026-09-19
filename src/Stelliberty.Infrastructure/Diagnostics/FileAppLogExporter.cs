using System.Text;
using Stelliberty.Application.Diagnostics;

namespace Stelliberty.Infrastructure.Diagnostics;

public sealed class FileAppLogExporter(params string[] logFilePaths) : IAppLogExporter
{
    public async Task ExportAsync(string exportPath, CancellationToken cancellationToken = default)
    {
        var fullExportPath = Path.GetFullPath(exportPath);
        // 禁止将导出目标指向运行日志，避免清空仍在写入的诊断信息。
        if (logFilePaths.Any(path => string.Equals(Path.GetFullPath(path), fullExportPath, StringComparison.OrdinalIgnoreCase)))
        {
            throw new IOException("The export path must be different from the running log paths.");
        }

        await using var target = new FileStream(
            exportPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);
        foreach (var logFilePath in logFilePaths)
        {
            await using var source = new FileStream(
                logFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 81920,
                useAsync: true);
            var header = Encoding.UTF8.GetBytes($"===== {Path.GetFileName(logFilePath)} ====={Environment.NewLine}");
            await target.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            await target.WriteAsync(Encoding.UTF8.GetBytes(Environment.NewLine), cancellationToken).ConfigureAwait(false);
        }
    }
}
