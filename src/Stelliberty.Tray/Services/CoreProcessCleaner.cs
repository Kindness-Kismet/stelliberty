using System.Diagnostics;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;

namespace Stelliberty.Tray;

public sealed class CoreProcessCleaner(string serviceDirectory)
{
    private const string CoreLockSuffix = ".lock";

    public CoreProcessCleanupResult CleanupForNormalMode(ServiceModeStatus serviceModeStatus)
    {
        if (serviceModeStatus.State == ServiceModeState.Unknown)
        {
            return CoreProcessCleanupResult.Failed($"Service status is unknown; normal-mode core startup was canceled: {serviceModeStatus.Message}");
        }

        if (serviceModeStatus.State == ServiceModeState.Running)
        {
            return CoreProcessCleanupResult.Skipped("Service status blocks normal-mode core cleanup.");
        }

        return CleanupLockedCores([]);
    }

    public CoreProcessCleanupResult CleanupForServiceMode(ServiceModeStatus serviceModeStatus)
    {
        return CleanupLockedCores(serviceModeStatus.CorePid is { } corePid ? [corePid] : []);
    }

    private CoreProcessCleanupResult CleanupLockedCores(IReadOnlyCollection<int> excludedProcessIds)
    {
        try
        {
            var currentPid = Environment.ProcessId;
            var processIds = FindLockedCoreProcessIds()
                .Where(processId => processId != currentPid && !excludedProcessIds.Contains(processId))
                .ToArray();
            var killedProcessIds = new List<int>();
            foreach (var processId in processIds)
            {
                if (TryKillLockedCore(processId))
                {
                    killedProcessIds.Add(processId);
                }
            }

            if (killedProcessIds.Count > 0)
            {
                AppLogger.Warning($"Cleaned up orphaned core processes: pid={string.Join(",", killedProcessIds)}");
            }

            return CoreProcessCleanupResult.Success(killedProcessIds);
        }
        catch (Exception exception)
        {
            return CoreProcessCleanupResult.Failed($"Orphaned core cleanup failed: {exception.Message}");
        }
    }

    private bool TryKillLockedCore(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
            RemoveCoreLock(processId);
            return true;
        }
        catch (ArgumentException)
        {
            RemoveCoreLock(processId);
            return false;
        }
        catch (InvalidOperationException)
        {
            RemoveCoreLock(processId);
            return false;
        }
    }

    private IEnumerable<int> FindLockedCoreProcessIds()
    {
        if (!Directory.Exists(serviceDirectory))
        {
            yield break;
        }

        foreach (var lockPath in Directory.EnumerateFiles(
            serviceDirectory,
            $"{AppRuntimeNames.CoreLockPrefix}*{CoreLockSuffix}",
            SearchOption.TopDirectoryOnly))
        {
            if (!TryGetCoreLockPid(lockPath, out var processId))
            {
                continue;
            }

            if (!IsProcessRunning(processId))
            {
                TryDelete(lockPath);
                continue;
            }

            yield return processId;
        }
    }

    private static bool TryGetCoreLockPid(string lockPath, out int processId)
    {
        processId = 0;
        var fileName = Path.GetFileName(lockPath);
        if (!fileName.StartsWith(AppRuntimeNames.CoreLockPrefix, StringComparison.Ordinal)
            || !fileName.EndsWith(CoreLockSuffix, StringComparison.Ordinal))
        {
            return false;
        }

        var pidText = fileName[AppRuntimeNames.CoreLockPrefix.Length..^CoreLockSuffix.Length];
        return int.TryParse(pidText, out processId) && processId > 0;
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private void RemoveCoreLock(int processId)
    {
        TryDelete(Path.Combine(
            serviceDirectory,
            $"{AppRuntimeNames.CoreLockPrefix}{processId}{CoreLockSuffix}"));
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            AppLogger.Warning($"Core lock delete failed: {Path.GetFileName(path)}");
        }
    }
}

public sealed record CoreProcessCleanupResult(bool IsSuccess, bool IsSkipped, string Message, IReadOnlyList<int> KilledProcessIds)
{
    public static CoreProcessCleanupResult Success(IReadOnlyList<int> killedProcessIds)
    {
        return new CoreProcessCleanupResult(true, false, string.Empty, killedProcessIds);
    }

    public static CoreProcessCleanupResult Skipped(string message)
    {
        return new CoreProcessCleanupResult(true, true, message, []);
    }

    public static CoreProcessCleanupResult Failed(string message)
    {
        return new CoreProcessCleanupResult(false, false, message, []);
    }
}
