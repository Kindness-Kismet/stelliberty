using Stelliberty.Application.Runtime;
using Stelliberty.Infrastructure.Tray;

namespace Stelliberty.Desktop.Services;

internal sealed class DesktopTraySession : IDisposable
{
    private readonly TrayIpcClient _client = new();
    private string? _sessionId;
    private int _isDisconnected;

    public event EventHandler? ActivationRequested;

    public event EventHandler? ToggleRequested;

    public event EventHandler? Disconnected;
    public event EventHandler<BackgroundTaskStatus>? BackgroundChanged;

    public bool CanExitToBackground { get; private set; }

    public bool IsDisconnected => Volatile.Read(ref _isDisconnected) != 0;

    public async Task RegisterAsync(string sessionToken, CancellationToken cancellationToken)
    {
        _client.ActivationRequested += OnActivationRequested;
        _client.ToggleRequested += OnToggleRequested;
        _client.Disconnected += OnDisconnected;
        _client.BackgroundChanged += OnBackgroundChanged;
        await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        var hello = await _client.HelloAsync(Environment.ProcessId, cancellationToken).ConfigureAwait(false);
        CanExitToBackground = hello.Capabilities.Contains("background_tray", StringComparer.Ordinal);
        var result = await _client.RegisterUiAsync(
            sessionToken,
            Environment.ProcessId,
            cancellationToken).ConfigureAwait(false);
        _sessionId = result.SessionId;
    }

    public async Task UnregisterAsync(CancellationToken cancellationToken)
    {
        if (_sessionId is null)
        {
            return;
        }

        await _client.UnregisterUiAsync(_sessionId, cancellationToken).ConfigureAwait(false);
        _sessionId = null;
    }

    public Task ShutdownTrayAsync(CancellationToken cancellationToken) =>
        _client.ShutdownAsync(cancellationToken);

    public Task<BackgroundTaskStatus> GetBackgroundStatusAsync(CancellationToken cancellationToken) =>
        _client.GetBackgroundStatusAsync(cancellationToken);

    private void OnBackgroundChanged(object? sender, BackgroundTaskStatus status) =>
        BackgroundChanged?.Invoke(this, status);

    private void OnActivationRequested(object? sender, EventArgs args) =>
        ActivationRequested?.Invoke(this, EventArgs.Empty);

    private void OnToggleRequested(object? sender, EventArgs args) =>
        ToggleRequested?.Invoke(this, EventArgs.Empty);

    private void OnDisconnected(object? sender, EventArgs args)
    {
        Interlocked.Exchange(ref _isDisconnected, 1);
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _client.ActivationRequested -= OnActivationRequested;
        _client.ToggleRequested -= OnToggleRequested;
        _client.Disconnected -= OnDisconnected;
        _client.BackgroundChanged -= OnBackgroundChanged;
        _client.Dispose();
        _sessionId = null;
        ActivationRequested = null;
        ToggleRequested = null;
        Disconnected = null;
    }
}
