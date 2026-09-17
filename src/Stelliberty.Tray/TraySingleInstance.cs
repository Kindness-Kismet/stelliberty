using Stelliberty.Infrastructure.Tray;
using Stelliberty.Application.Platform;

namespace Stelliberty.Tray;

internal sealed class TraySingleInstance : IDisposable
{
    private readonly FileStream? _lockStream;
    private readonly Mutex? _mutex;

    public TraySingleInstance()
    {
        if (OperatingSystem.IsWindows())
        {
            // 与旧版本及安装程序使用同一互斥量，升级时不能并行启动两个宿主。
            _mutex = new Mutex(true, $@"Global\{AppMetadata.Name}.{AppRuntimeNames.ChannelName}.SingleInstance", out var ownsInstance);
            OwnsInstance = ownsInstance;
            return;
        }
        TrayEndpoint.PrepareRuntimeDirectory();
        try
        {
            _lockStream = new FileStream(
                TrayEndpoint.LockFilePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            OwnsInstance = true;
        }
        catch (IOException)
        {
            OwnsInstance = false;
        }
    }

    public bool OwnsInstance { get; }

    public void Dispose()
    {
        _lockStream?.Dispose();
        if (_mutex is not null)
        {
            if (OwnsInstance)
            {
                _mutex.ReleaseMutex();
            }
            _mutex.Dispose();
        }
    }
}
