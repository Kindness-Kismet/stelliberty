using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;

namespace Stelliberty.Tray;

internal interface IDesktopUiLauncher
{
    Task LaunchAsync(string sessionToken, CancellationToken cancellationToken);
}

internal sealed class DesktopUiLauncher : IDesktopUiLauncher, IAsyncDisposable
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(2);
    private readonly IDesktopUiProcessStarter _processStarter;
    private readonly Func<string> _resolveExecutablePath;
    private readonly TimeSpan _shutdownTimeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IDesktopUiProcess? _process;
    private bool _isDisposed;

    public DesktopUiLauncher()
        : this(new SystemDesktopUiProcessStarter(), ResolveDesktopExecutable, ShutdownTimeout)
    {
    }

    internal DesktopUiLauncher(
        IDesktopUiProcessStarter processStarter,
        Func<string> resolveExecutablePath,
        TimeSpan shutdownTimeout)
    {
        _processStarter = processStarter;
        _resolveExecutablePath = resolveExecutablePath;
        _shutdownTimeout = shutdownTimeout;
    }

    public async Task LaunchAsync(string sessionToken, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            await StopOwnedProcessAsync().ConfigureAwait(false);
            var startInfo = new ProcessStartInfo(_resolveExecutablePath())
            {
                UseShellExecute = false,
                WorkingDirectory = AppContext.BaseDirectory,
            };
            startInfo.ArgumentList.Add("--ui");
            startInfo.ArgumentList.Add("--tray-session");
            startInfo.ArgumentList.Add(sessionToken);
            _process = _processStarter.Start(startInfo);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string ResolveDesktopExecutable()
    {
        var packagedPath = Path.Combine(
            AppContext.BaseDirectory,
            PathConventions.DataDirectoryName,
            PathConventions.DepsSubdirectory,
            AppRuntimeNames.UiBinaryName);
        if (File.Exists(packagedPath))
        {
            return packagedPath;
        }

#if DEBUG
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "Stelliberty.Desktop",
                "bin",
                "Debug",
                "net11.0",
                AppRuntimeNames.UiBinaryName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }
#endif

        throw new FileNotFoundException("Desktop executable was not found.", packagedPath);
    }

    private async Task StopOwnedProcessAsync()
    {
        if (_process is not { } process)
        {
            return;
        }

        _process = null;
        try
        {
            if (!process.HasExited)
            {
                using var timeout = new CancellationTokenSource(_shutdownTimeout);
                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                {
                }
            }

            if (!process.HasExited)
            {
                AppLogger.Warning($"Desktop UI did not exit with the tray; terminating pid={process.Id}");
                process.Kill();
            }
        }
        finally
        {
            process.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopOwnedProcessAsync().ConfigureAwait(false);
        }
        finally
        {
            _processStarter.Dispose();
            _gate.Release();
        }
    }
}

internal interface IDesktopUiProcessStarter : IDisposable
{
    IDesktopUiProcess Start(ProcessStartInfo startInfo);
}

internal interface IDesktopUiProcess : IDisposable
{
    int Id { get; }

    bool HasExited { get; }

    Task WaitForExitAsync(CancellationToken cancellationToken);

    void Kill();
}

internal sealed class SystemDesktopUiProcessStarter : IDesktopUiProcessStarter
{
    private readonly WindowsUiProcessJob? _windowsJob;

    public SystemDesktopUiProcessStarter()
    {
        if (OperatingSystem.IsWindows())
        {
            _windowsJob = new WindowsUiProcessJob();
        }
    }

    public IDesktopUiProcess Start(ProcessStartInfo startInfo)
    {
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Desktop UI process could not be started.");
        try
        {
            _windowsJob?.Assign(process);
            return new SystemDesktopUiProcess(process);
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill();
            }

            process.Dispose();
            throw;
        }
    }

    public void Dispose() => _windowsJob?.Dispose();
}

internal sealed class SystemDesktopUiProcess(Process process) : IDesktopUiProcess
{
    public int Id => process.Id;

    public bool HasExited => process.HasExited;

    public Task WaitForExitAsync(CancellationToken cancellationToken) =>
        process.WaitForExitAsync(cancellationToken);

    public void Kill()
    {
        try
        {
            process.Kill();
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
        }
    }

    public void Dispose() => process.Dispose();
}

// Windows 在托盘被强制终止时关闭 Job Object，由系统回收仍存活的 UI。
internal sealed class WindowsUiProcessJob : IDisposable
{
    private const uint SilentBreakawayOk = 0x00001000;
    private const uint KillOnJobClose = 0x00002000;
    private readonly SafeFileHandle _handle;

    public WindowsUiProcessJob()
    {
        _handle = CreateJobObject(nint.Zero, null);
        if (_handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Desktop UI Job Object could not be created.");
        }

        var information = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation
            {
                // UI 启动的浏览器等外部进程不能继承托盘的退出约束。
                LimitFlags = SilentBreakawayOk | KillOnJobClose,
            },
        };
        if (!SetInformationJobObject(
                _handle,
                JobObjectInformationClass.ExtendedLimitInformation,
                ref information,
                (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
        {
            var error = Marshal.GetLastWin32Error();
            _handle.Dispose();
            throw new Win32Exception(error, "Desktop UI Job Object could not be configured.");
        }
    }

    public void Assign(Process process)
    {
        if (!AssignProcessToJobObject(_handle, process.SafeHandle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Desktop UI process could not join its Job Object.");
        }
    }

    public void Dispose() => _handle.Dispose();

    private enum JobObjectInformationClass
    {
        ExtendedLimitInformation = 9,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(nint jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        SafeFileHandle job,
        JobObjectInformationClass informationClass,
        ref JobObjectExtendedLimitInformation information,
        uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);
}
