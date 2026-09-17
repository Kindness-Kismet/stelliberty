namespace Stelliberty.Tray;

internal sealed class TrayLifetime : IDisposable
{
    private readonly CancellationTokenSource _stopping = new();

    public CancellationToken StoppingToken => _stopping.Token;

    public void RequestStop() => _stopping.Cancel();

    public void Dispose() => _stopping.Dispose();
}
