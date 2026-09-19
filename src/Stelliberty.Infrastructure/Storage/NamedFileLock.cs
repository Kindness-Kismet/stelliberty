using System.Security.Cryptography;
using System.Text;

namespace Stelliberty.Infrastructure.Storage;

// 同一路径的读改写跨实例、跨宿主互斥；锁的获取与释放必须在同一线程。
internal sealed class NamedFileLock : IDisposable
{
    private readonly Mutex _mutex;

    public NamedFileLock(string path)
    {
        var normalized = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows()) normalized = normalized.ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        _mutex = new Mutex(false, $"Stelliberty.File.{hash}");
        try
        {
            _mutex.WaitOne();
        }
        catch (AbandonedMutexException)
        {
            // 前一宿主异常退出时，当前线程已获得锁。
        }
    }

    public void Dispose()
    {
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
