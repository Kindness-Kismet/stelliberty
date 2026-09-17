using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;

namespace Stelliberty.Infrastructure.Platform;

[SupportedOSPlatform("windows")]
public sealed class WindowsAppBehaviorService : IAppBehaviorService
{
    private static readonly TimeSpan SchtasksTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ElevatedSchtasksTimeout = TimeSpan.FromMinutes(2);
    private readonly string _binaryPath;

    public WindowsAppBehaviorService(string binaryPath) => _binaryPath = binaryPath;

    public void Apply(AppBehaviorApplicationRequest request)
    {
        ApplyAutoStart(request.IsAutoStartEnabled, request.IsSilentStartEnabled);
        AppLogger.Info($"Windows app behavior applied: autoStart={request.IsAutoStartEnabled}");
    }

    private void ApplyAutoStart(bool isEnabled, bool isSilentStartEnabled)
    {
        if (!isEnabled)
        {
            if (!DeleteScheduledTask())
            {
                throw new InvalidOperationException("Windows autostart scheduled task deletion failed.");
            }

            return;
        }

        if (!RegisterScheduledTask(_binaryPath, isSilentStartEnabled))
        {
            throw new InvalidOperationException("Windows autostart scheduled task registration failed.");
        }
    }

    private static bool RegisterScheduledTask(string binaryPath, bool isSilentStartEnabled)
    {
        var xmlPath = Path.Combine(
            Path.GetTempPath(),
            $"{AutoStartEntryBuilder.WindowsTaskFilePrefix}-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(
                xmlPath,
                AutoStartEntryBuilder.WindowsScheduledTaskXml(
                    binaryPath,
                    CurrentUserSid(),
                    isSilentStartEnabled),
                Encoding.Unicode);
            var result = RunSchtasks(
                ["/create", "/tn", AutoStartEntryBuilder.WindowsTaskName, "/xml", xmlPath, "/f"],
                requiresAdministrator: true);
            if (result.IsSuccess)
            {
                AppLogger.Info($"Windows autostart scheduled task registered: {AutoStartEntryBuilder.WindowsTaskName}");
                return true;
            }

            AppLogger.Warning($"Windows autostart scheduled task registration failed: {result.Message}");
            return false;
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Windows autostart scheduled task registration failed: {exception.Message}");
            return false;
        }
        finally
        {
            TryDelete(xmlPath);
        }
    }

    private static bool DeleteScheduledTask()
    {
        if (!IsScheduledTaskRegistered())
        {
            return true;
        }

        var result = RunSchtasks(
            ["/delete", "/tn", AutoStartEntryBuilder.WindowsTaskName, "/f"],
            requiresAdministrator: true);
        if (!result.IsSuccess || IsScheduledTaskRegistered())
        {
            AppLogger.Warning($"Windows autostart scheduled task deletion failed: {result.Message}");
            return false;
        }

        return true;
    }

    private static bool IsScheduledTaskRegistered()
    {
        return RunSchtasks(["/query", "/tn", AutoStartEntryBuilder.WindowsTaskName]).IsSuccess;
    }

    private static SchtasksResult RunSchtasks(IReadOnlyList<string> arguments, bool requiresAdministrator = false)
    {
        return requiresAdministrator && !IsAdministrator()
            ? RunElevatedSchtasks(arguments)
            : RunDirectSchtasks(arguments);
    }

    private static SchtasksResult RunDirectSchtasks(IReadOnlyList<string> arguments)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo(SchtasksPath())
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(SchtasksTimeout))
            {
                process.Kill(entireProcessTree: true);
                return new SchtasksResult(false, "schtasks.exe timed out.");
            }

            // WaitForExit 成功后管道已关闭，读取任务必然已完成，不会真阻塞。
            var output = outputTask.GetAwaiter().GetResult();
            var error = errorTask.GetAwaiter().GetResult();
            return new SchtasksResult(process.ExitCode == 0, string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim());
        }
        catch (Exception exception)
        {
            return new SchtasksResult(false, exception.Message);
        }
    }

    private static SchtasksResult RunElevatedSchtasks(IReadOnlyList<string> arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(SchtasksPath())
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = CommandLine(arguments),
                WindowStyle = ProcessWindowStyle.Hidden
            });
            if (process is null)
            {
                return new SchtasksResult(false, "schtasks.exe failed to start.");
            }

            if (!process.WaitForExit(ElevatedSchtasksTimeout))
            {
                process.Kill(entireProcessTree: true);
                return new SchtasksResult(false, "schtasks.exe timed out.");
            }

            return new SchtasksResult(process.ExitCode == 0, $"exit code {process.ExitCode}");
        }
        catch (Exception exception)
        {
            return new SchtasksResult(false, exception.Message);
        }
    }

    private static string CommandLine(IReadOnlyList<string> arguments)
    {
        return string.Join(' ', arguments.Select(CommandLineArgument));
    }

    private static string CommandLineArgument(string value)
    {
        return string.IsNullOrEmpty(value) || value.Any(char.IsWhiteSpace)
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
    }

    private static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Windows autostart privilege detection failed: {exception.Message}");
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Windows autostart temporary file cleanup failed: {exception.Message}");
        }
    }

    private static string? CurrentUserSid()
    {
        try
        {
            return WindowsIdentity.GetCurrent().User?.Value;
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Windows autostart user detection failed: {exception.Message}");
            return null;
        }
    }

    private static string SchtasksPath()
    {
        return Path.Combine(Environment.SystemDirectory, "schtasks.exe");
    }

    private sealed record SchtasksResult(bool IsSuccess, string Message);
}
