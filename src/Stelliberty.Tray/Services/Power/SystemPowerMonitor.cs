using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Runtime;

namespace Stelliberty.Tray;

internal interface ISystemPowerEventSource : IAsyncDisposable
{
    string Status { get; }
    Task StartAsync();
}

internal sealed class SystemPowerMonitor(SystemPowerRecoveryService recovery) : IAsyncDisposable
{
    private ISystemPowerEventSource? _source;
    private string? _initializationError;

    public string Status => _initializationError ?? _source?.Status ?? "not-started";

    public async Task StartAsync()
    {
        try
        {
            _source = OperatingSystem.IsWindows() ? new WindowsPowerEventSource(Report)
                : OperatingSystem.IsMacOS() ? new MacOSPowerEventSource(Report)
                : OperatingSystem.IsLinux() ? new LinuxPowerEventSource(Report)
                : null;
            if (_source is null)
            {
                _initializationError = "unsupported-platform";
                AppLogger.Warning("Power monitoring is unavailable on this platform.");
                return;
            }
            await _source.StartAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _initializationError = $"unavailable: {exception.Message}";
            AppLogger.Warning($"Power monitor initialization failed: {exception}");
        }
    }

    private void Report(SystemPowerEventKind kind, string source)
    {
        try
        {
            _ = recovery.HandleAsync(kind, source);
        }
        catch (Exception exception)
        {
            // 托管异常不能越过系统通知回调边界。
            AppLogger.Error(exception, $"Power event dispatch failed: source={source} event={kind}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_source is not null) await _source.DisposeAsync().ConfigureAwait(false);
    }
}
