using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Runtime;

namespace Stelliberty.Tray;

public sealed record ServiceModePaths(
    string ServiceDirectory,
    string UpdateBinaryPath,
    string InstalledBinaryPath);

public sealed class ServiceModeManager(ServiceModePaths paths) : IServiceModeManager
{
    // 覆盖 status、heartbeat、version 这类非提权只读查询；提权与生命周期操作用 ManageTimeout。
    private static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(5);
    // 远短于 2s 轮询间隔，避免请求堆叠。
    private static readonly TimeSpan ObserveTimeout = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan ManageTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan StatusSettleTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StatusPollInterval = TimeSpan.FromMilliseconds(250);
    private const string PrivilegeCanceledMessage = "Administrator approval was canceled; no changes were made.";
    // 安装包替换服务文件后按文件特征失效缓存，避免每次状态刷新重复启动进程。
    private readonly object _serviceVersionCacheLock = new();
    private DateTime _serviceVersionFileLastWriteUtc;
    private long _serviceVersionFileLength = -1;
    private string? _cachedServiceVersion;

    public async Task<CoreObservation> ObserveCoreAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = await ServiceCommandPipeClient
                .RequestStatusAsync(ObserveTimeout, cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(payload.ServiceName, AppRuntimeNames.ServiceName, StringComparison.Ordinal))
            {
                return CoreObservation.Unobserved($"service name does not match: {payload.ServiceName}");
            }

            return CoreObservation.Observed(
                ParseCoreState(payload.CoreState),
                payload.CorePid,
                payload.CoreLastError);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return CoreObservation.Unobserved(DescribeObserveFailure(exception));
        }
    }

    public async Task<ServiceModeStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var repairStatus = DetectServiceRepairStatus();
        if (repairStatus is not null)
        {
            return repairStatus;
        }

        var installedBinaryPath = InstalledServiceBinaryPath();
        if (installedBinaryPath is null)
        {
            return new ServiceModeStatus(ServiceModeState.NotInstalled, "Service is not installed.");
        }

        var result = await RunServiceCommandAsync(
            "status",
            false,
            StatusTimeout,
            cancellationToken,
            binaryPath: installedBinaryPath).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return ServiceModeStatus.Unavailable(result.Message);
        }

        var message = result.Message.Trim();
        var parts = ParseServiceStatus(message);
        var isInstalledState = message.StartsWith("running", StringComparison.OrdinalIgnoreCase)
            || message.StartsWith("stopped", StringComparison.OrdinalIgnoreCase);
        var availableVersion = isInstalledState
            ? await GetAvailableServiceVersionAsync(cancellationToken).ConfigureAwait(false)
            : null;
        if (message.StartsWith("running", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.ServiceName is not null
                && !string.Equals(parts.ServiceName, AppRuntimeNames.ServiceName, StringComparison.Ordinal))
            {
                return ServiceModeStatus.Unavailable($"Service name does not match: {parts.ServiceName}");
            }

            return new ServiceModeStatus(
                ServiceModeState.Running,
                "Service is running.",
                parts.Uptime,
                parts.LastHeartbeatAge,
                parts.CoreState,
                parts.CorePid,
                parts.CoreLastError,
                parts.Version,
                availableVersion);
        }

        if (message.StartsWith("stopped", StringComparison.OrdinalIgnoreCase))
        {
            return new ServiceModeStatus(
                ServiceModeState.Stopped,
                "Service is installed but not running.",
                InstalledVersion: parts.Version,
                AvailableVersion: availableVersion);
        }

        if (message.StartsWith("not-installed", StringComparison.OrdinalIgnoreCase))
        {
            return new ServiceModeStatus(ServiceModeState.NotInstalled, "Service is not installed.");
        }

        return ServiceModeStatus.Unavailable(result.Message);
    }

    public async Task<ServiceModeOperationResult> InstallOrUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(paths.UpdateBinaryPath))
        {
            return ServiceModeOperationResult.Failed("Service executable is missing. Run prebuild or rebuild the app first.");
        }

        var status = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (status.State == ServiceModeState.Unknown)
        {
            return ServiceModeOperationResult.Failed($"Could not confirm service-mode status; update was canceled: {status.Message}");
        }

        if (status.NeedsRepair)
        {
            return ServiceModeOperationResult.Failed("Service mode needs repair before it can be installed or updated.");
        }

        if (status.IsRunning)
        {
            var stopResult = await RunServiceCommandAsync("stop", true, ManageTimeout, cancellationToken).ConfigureAwait(false);
            if (!stopResult.IsSuccess)
            {
                return stopResult;
            }
        }

        var copyResult = CopyServiceUpdateToInstalled();
        if (!copyResult.IsSuccess)
        {
            return copyResult;
        }

        var result = await RunServiceCommandAsync(
            "install",
            true,
            ManageTimeout,
            cancellationToken,
            binaryPath: paths.InstalledBinaryPath).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return result;
        }

        var installedStatus = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return installedStatus.IsHealthy
            ? ServiceModeOperationResult.Success("Service mode is installed and running.")
            : ServiceModeOperationResult.Failed($"Service was installed but did not respond: {installedStatus.Message}");
    }

    public async Task<ServiceModeOperationResult> UninstallAsync(CancellationToken cancellationToken = default)
    {
        var commandBinaryPath = File.Exists(paths.InstalledBinaryPath)
            ? paths.InstalledBinaryPath
            : File.Exists(paths.UpdateBinaryPath)
                ? paths.UpdateBinaryPath
                : null;
        if (commandBinaryPath is null)
        {
            var repairStatus = DetectServiceRepairStatus();
            if (repairStatus?.State is ServiceModeState.NeedsRepair or ServiceModeState.Unknown)
            {
                return ServiceModeOperationResult.Failed("Service executable is missing; repair cannot continue.");
            }

            return ServiceModeOperationResult.Success("Service mode is not installed.");
        }

        var result = await RunServiceCommandAsync(
            "uninstall",
            true,
            ManageTimeout,
            cancellationToken,
            binaryPath: commandBinaryPath).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return result;
        }

        var status = await WaitForStatusAsync(
            state => state.State == ServiceModeState.NotInstalled,
            StatusSettleTimeout,
            cancellationToken).ConfigureAwait(false);
        return status.State == ServiceModeState.NotInstalled
            ? ServiceModeOperationResult.Success("Service mode is uninstalled.")
            : ServiceModeOperationResult.Failed($"Service was uninstalled but is still visible: {status.Message}");
    }

    public Task<ServiceModeOperationResult> StartCoreHostAsync(ServiceModeCoreHostRequest request, CancellationToken cancellationToken = default)
    {
        return RunServiceCommandAsync("start-core", false, ManageTimeout, cancellationToken, SerializeStartCorePayload(request));
    }

    public Task<ServiceModeOperationResult> ApplyCoreConfigAsync(string configPath, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new ApplyCoreConfigCommand(new ApplyCoreConfigPayload(configPath)));
        return RunServiceCommandAsync("apply-core-config", false, ManageTimeout, cancellationToken, payload);
    }

    private static string SerializeStartCorePayload(ServiceModeCoreHostRequest request)
    {
        return JsonSerializer.Serialize(
            new StartCoreCommand(
                new StartCorePayload(
                    request.CorePath,
                    request.ConfigPath,
                    request.DataCoreDir)));
    }

    public Task<ServiceModeOperationResult> StopCoreHostAsync(CancellationToken cancellationToken = default)
    {
        return RunServiceCommandAsync("stop-core", false, ManageTimeout, cancellationToken);
    }

    public Task<ServiceModeOperationResult> RestartCoreHostAsync(CancellationToken cancellationToken = default)
    {
        return RunServiceCommandAsync("restart-core", false, ManageTimeout, cancellationToken);
    }

    public Task<ServiceModeOperationResult> SendHeartbeatAsync(CancellationToken cancellationToken = default)
    {
        return RunServiceCommandAsync("heartbeat", false, StatusTimeout, cancellationToken);
    }

    private async Task<ServiceModeOperationResult> RunServiceCommandAsync(
        string command,
        bool elevated,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        string? standardInput = null,
        string? binaryPath = null)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);
            var selectedBinaryPath = binaryPath ?? InstalledServiceBinaryPath();
            if (selectedBinaryPath is null)
            {
                return ServiceModeOperationResult.Failed("Service executable is missing. Run prebuild or rebuild the app first.");
            }

            using var process = StartProcess(command, elevated, selectedBinaryPath);
            if (process is null)
            {
                return ServiceModeOperationResult.Failed("Service command failed to start.");
            }

            if (standardInput is not null && process.StartInfo.RedirectStandardInput)
            {
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), cts.Token).ConfigureAwait(false);
                await process.StandardInput.DisposeAsync().ConfigureAwait(false);
            }

            var outputTask = process.StartInfo.RedirectStandardOutput
                ? process.StandardOutput.ReadToEndAsync(cts.Token)
                : Task.FromResult(string.Empty);
            var errorTask = process.StartInfo.RedirectStandardError
                ? process.StandardError.ReadToEndAsync(cts.Token)
                : Task.FromResult(string.Empty);

            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);

            if (process.ExitCode == 0)
            {
                return ServiceModeOperationResult.Success(string.IsNullOrWhiteSpace(output) ? "Service command completed." : output.Trim());
            }

            if (IsPrivilegeCanceled(process.ExitCode, output, error))
            {
                return ServiceModeOperationResult.Canceled(PrivilegeCanceledMessage);
            }

            return ServiceModeOperationResult.Failed(string.IsNullOrWhiteSpace(error) ? $"Service command failed with exit code {process.ExitCode}." : error.Trim());
        }
        catch (Win32Exception exception) when (OperatingSystem.IsWindows() && exception.NativeErrorCode == 1223)
        {
            return ServiceModeOperationResult.Canceled(PrivilegeCanceledMessage);
        }
        catch (OperationCanceledException)
        {
            return ServiceModeOperationResult.Failed("Service command timed out.");
        }
        catch (Exception exception)
        {
            const string failure = "Service command failed";
            AppLogger.Error(exception, failure);
            return ServiceModeOperationResult.Failed($"{failure}: {exception.Message}");
        }
    }

    private ServiceModeOperationResult CopyServiceUpdateToInstalled()
    {
        try
        {
            Directory.CreateDirectory(paths.ServiceDirectory);
            File.Copy(
                paths.UpdateBinaryPath,
                paths.InstalledBinaryPath,
                overwrite: true);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    paths.InstalledBinaryPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            return ServiceModeOperationResult.Success("Service executable was updated.");
        }
        catch (Exception exception)
        {
            const string failure = "Service executable update failed";
            AppLogger.Error(exception, failure);
            return ServiceModeOperationResult.Failed($"{failure}: {exception.Message}");
        }
    }

    private static CoreState ParseCoreState(string? value)
    {
        return value switch
        {
            "running" => CoreState.Running,
            "stopping" => CoreState.Stopping,
            "stopped" => CoreState.Stopped,
            "crashed" => CoreState.Crashed,
            // 服务报了个本版本不认识的状态，属于观测不到而非核心异常。
            _ => CoreState.Unavailable,
        };
    }

    private static string DescribeObserveFailure(Exception exception)
    {
        return exception is OperationCanceledException
            ? "service pipe request timed out"
            : $"{exception.GetType().Name}: {exception.Message}";
    }

    private string? InstalledServiceBinaryPath()
    {
        return File.Exists(paths.InstalledBinaryPath)
            ? paths.InstalledBinaryPath
            : null;
    }

    private async Task<string?> GetAvailableServiceVersionAsync(CancellationToken cancellationToken)
    {
        var path = paths.UpdateBinaryPath;
        if (!File.Exists(path))
        {
            return null;
        }

        var fileInfo = new FileInfo(path);
        var lastWriteUtc = fileInfo.LastWriteTimeUtc;
        var length = fileInfo.Length;
        lock (_serviceVersionCacheLock)
        {
            if (_serviceVersionFileLength == length
                && _serviceVersionFileLastWriteUtc == lastWriteUtc)
            {
                return _cachedServiceVersion;
            }
        }

        var result = await RunServiceCommandAsync(
            "version",
            false,
            StatusTimeout,
            cancellationToken,
            binaryPath: path).ConfigureAwait(false);
        var version = result.IsSuccess ? ParseServiceVersion(result.Message) : null;
        if (version is not null)
        {
            lock (_serviceVersionCacheLock)
            {
                _serviceVersionFileLastWriteUtc = lastWriteUtc;
                _serviceVersionFileLength = length;
                _cachedServiceVersion = version;
            }
        }

        return version;
    }

    private static string? ParseServiceVersion(string message)
    {
        var parts = message.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[^1] : null;
    }

    private ServiceModeStatus? DetectServiceRepairStatus()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        RegistryKey? key;
        try
        {
            key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{AppRuntimeNames.ServiceName}");
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Service config probe failed: {exception.Message}");
            return ServiceModeStatus.Unavailable($"Service configuration is unreadable: {exception.Message}");
        }

        using (key)
        {
            if (key is null)
            {
                return null;
            }

            if (key.GetValue("ImagePath") is not string imagePath || string.IsNullOrWhiteSpace(imagePath))
            {
                return new ServiceModeStatus(ServiceModeState.NeedsRepair, "Service mode is installed but needs repair.");
            }

            string configuredPath;
            try
            {
                configuredPath = NormalizeServiceExecutablePath(imagePath);
            }
            catch (Exception exception)
            {
                AppLogger.Warning($"Service image path is invalid: {exception.Message}");
                return new ServiceModeStatus(ServiceModeState.NeedsRepair, "Service mode is installed but needs repair.");
            }

            var expectedPath = Path.GetFullPath(paths.InstalledBinaryPath);
            if (!File.Exists(paths.InstalledBinaryPath))
            {
                return new ServiceModeStatus(ServiceModeState.NeedsRepair, "Service mode is installed but needs repair.");
            }

            return SameServicePath(configuredPath, expectedPath)
                ? null
                : new ServiceModeStatus(ServiceModeState.NeedsRepair, "Service mode is installed for another location and needs repair.");
        }
    }

    private static string NormalizeServiceExecutablePath(string value)
    {
        var expanded = Environment.ExpandEnvironmentVariables(value).Trim();
        var executablePath = ExtractServiceExecutablePath(expanded);
        return Path.GetFullPath(executablePath);
    }

    private static string ExtractServiceExecutablePath(string value)
    {
        if (value.StartsWith('"'))
        {
            var endQuote = value.IndexOf('"', 1);
            return endQuote > 1 ? value[1..endQuote] : value.Trim('"');
        }

        var exeIndex = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exeIndex >= 0 ? value[..(exeIndex + ".exe".Length)] : value;
    }

    private static bool SameServicePath(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ServiceModeStatus> WaitForStatusAsync(
        Func<ServiceModeStatus, bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        ServiceModeStatus status;
        do
        {
            status = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
            if (predicate(status))
            {
                return status;
            }

            await Task.Delay(StatusPollInterval, cancellationToken).ConfigureAwait(false);
        }
        while (stopwatch.Elapsed < timeout);

        return status;
    }

    private static Process? StartProcess(string command, bool elevated, string binaryPath)
    {
#if DEBUG
        if (elevated && IsCiServiceCommandEnabled())
        {
            return OperatingSystem.IsWindows()
                ? StartDirectServiceCommand(command, binaryPath)
                : StartSudoServiceCommand(command, binaryPath);
        }
#endif

        if (!elevated)
        {
            return StartDirectServiceCommand(command, binaryPath);
        }

        if (OperatingSystem.IsWindows())
        {
            return StartWindowsElevated(command, binaryPath);
        }

        if (OperatingSystem.IsMacOS())
        {
            return StartMacOSElevated(command, binaryPath);
        }

        return StartUnixElevated(command, binaryPath);
    }

    private static Process? StartDirectServiceCommand(string command, string binaryPath)
    {
        var info = new ProcessStartInfo(binaryPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = command is "start-core" or "apply-core-config",
            CreateNoWindow = true,
            // 服务端输出 UTF-8；Windows 默认按 ANSI 解码会把中文系统错误变成乱码。
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        info.ArgumentList.Add(command);
        return Process.Start(info);
    }

#if DEBUG
    private static bool IsCiServiceCommandEnabled()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("STELLIBERTY_DEBUG_SERVICE_CI"),
            "1",
            StringComparison.Ordinal);
    }

    private static Process? StartSudoServiceCommand(string command, string binaryPath)
    {
        var info = new ProcessStartInfo("sudo")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add("-n");
        info.ArgumentList.Add(binaryPath);
        info.ArgumentList.Add(command);
        return Process.Start(info);
    }
#endif

    private static (string? ServiceName, string? Version, TimeSpan? Uptime, TimeSpan? LastHeartbeatAge, string? CoreState, int? CorePid, string? CoreLastError) ParseServiceStatus(string message)
    {
        string? serviceName = null;
        string? version = null;
        TimeSpan? uptime = null;
        TimeSpan? lastHeartbeatAge = null;
        string? coreState = null;
        int? corePid = null;
        string? coreLastError = null;
        var errorIndex = message.IndexOf(" error=", StringComparison.OrdinalIgnoreCase);
        if (errorIndex >= 0)
        {
            coreLastError = message[(errorIndex + " error=".Length)..];
            message = message[..errorIndex];
        }

        foreach (var part in message.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith("service=", StringComparison.OrdinalIgnoreCase))
            {
                serviceName = part["service=".Length..];
                continue;
            }

            if (part.StartsWith("uptime=", StringComparison.OrdinalIgnoreCase))
            {
                uptime = ParseSeconds(part["uptime=".Length..]);
                continue;
            }

            if (part.StartsWith("version=", StringComparison.OrdinalIgnoreCase))
            {
                version = part["version=".Length..];
                continue;
            }

            if (part.StartsWith("heartbeat=", StringComparison.OrdinalIgnoreCase))
            {
                var value = part["heartbeat=".Length..];
                lastHeartbeatAge = value.Equals("none", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : ParseSeconds(value);
                continue;
            }

            if (part.StartsWith("core=", StringComparison.OrdinalIgnoreCase))
            {
                coreState = part["core=".Length..];
                continue;
            }

            if (part.StartsWith("pid=", StringComparison.OrdinalIgnoreCase))
            {
                var value = part["pid=".Length..];
                corePid = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) && pid > 0 ? pid : null;
                continue;
            }

        }

        return (serviceName, version, uptime, lastHeartbeatAge, coreState, corePid, coreLastError);
    }

    private static TimeSpan? ParseSeconds(string value)
    {
        if (!value.EndsWith('s'))
        {
            return null;
        }

        var secondsText = value[..^1];
        if (!long.TryParse(secondsText, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) || seconds < 0)
        {
            return null;
        }

        try
        {
            return TimeSpan.FromSeconds(seconds);
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static Process? StartWindowsElevated(string command, string binaryPath)
    {
        return Process.Start(new ProcessStartInfo(binaryPath)
        {
            UseShellExecute = true,
            Verb = "runas",
            Arguments = command,
        });
    }

    [SupportedOSPlatform("macos")]
    private static Process? StartMacOSElevated(string command, string binaryPath)
    {
        var script = $"do shell script {QuoteAppleScript(ShellQuote(binaryPath) + " " + ShellQuote(command))} with administrator privileges";
        var info = new ProcessStartInfo("osascript")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add("-e");
        info.ArgumentList.Add(script);
        return Process.Start(info);
    }

    private static Process? StartUnixElevated(string command, string binaryPath)
    {
        var info = new ProcessStartInfo("pkexec")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add(binaryPath);
        info.ArgumentList.Add(command);
        return Process.Start(info);
    }

    private static string ShellQuote(string value)
    {
        return "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    }

    private static bool IsPrivilegeCanceled(int exitCode, string output, string error)
    {
        var text = output + "\n" + error;
        if (OperatingSystem.IsMacOS())
        {
            return text.Contains("User canceled", StringComparison.OrdinalIgnoreCase)
                || text.Contains("-128", StringComparison.Ordinal);
        }

        if (OperatingSystem.IsLinux())
        {
            return exitCode == 126
                && (text.Contains("cancel", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("dismiss", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("not authorized", StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    private static string QuoteAppleScript(string value)
    {
        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    // 服务 spawn 核心所需的完整启动参数
    private sealed record StartCoreCommand(
        [property: JsonPropertyName("data")] StartCorePayload Data)
    {
        [JsonPropertyName("type")]
        public string Type => "StartCore";
    }

    private sealed record StartCorePayload(
        [property: JsonPropertyName("mihomo_path")] string CorePath,
        [property: JsonPropertyName("config_path")] string ConfigPath,
        [property: JsonPropertyName("data_core_dir")] string DataCoreDir);

    private sealed record ApplyCoreConfigCommand(
        [property: JsonPropertyName("data")] ApplyCoreConfigPayload Data)
    {
        [JsonPropertyName("type")]
        public string Type => "ApplyCoreConfig";
    }

    private sealed record ApplyCoreConfigPayload(
        [property: JsonPropertyName("config_path")] string ConfigPath);
}
