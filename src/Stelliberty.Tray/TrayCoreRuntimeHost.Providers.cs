using System.Text.Json;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Runtime;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Infrastructure.Subscriptions;
using Stelliberty.Infrastructure.Tray;

namespace Stelliberty.Tray;

internal sealed partial class TrayCoreRuntimeHost
{
    private readonly PipeCoreProviderClient _providerClient = new(TrayCoreEndpoints.Core);
    private readonly SubscriptionProviderSnapshotService _providerSnapshots = new(
        new FileSubscriptionStore(TrayApplicationLayout.AppDataDirectory),
        new FileSubscriptionProviderSnapshotStore(TrayApplicationLayout.RuntimeDirectory),
        new SubscriptionProviderParser());
    private SubscriptionProviderSnapshot? _providerContext;
    private long _providerRevision;

    public async Task<SubscriptionProviderSnapshot> ReadProvidersAsync(string subscriptionId, CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            return _providerContext?.SubscriptionId == subscriptionId && CurrentStatus.Snapshot.State == CoreState.Running
                ? await CaptureProvidersCoreAsync(cancellationToken).ConfigureAwait(false)
                : _providerSnapshots.ReadCached(subscriptionId);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<long> RefreshProvidersAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_providerContext is not null && CurrentStatus.Snapshot.State == CoreState.Running)
            {
                await CaptureProvidersCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            return _providerRevision;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task SyncProviderAsync(string subscriptionId, string providerType, string providerName, CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_providerContext?.SubscriptionId != subscriptionId || CurrentStatus.Snapshot.State != CoreState.Running)
            {
                throw new InvalidOperationException("Provider subscription is not running.");
            }
            var provider = _providerContext.Providers.FirstOrDefault(item => item.Type == providerType && item.Name == providerName)
                ?? throw new InvalidOperationException("Provider is no longer present in the running configuration.");
            await _providerClient.SyncAsync(provider, cancellationToken).ConfigureAwait(false);
            await CaptureProvidersCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    // 读取与配置应用共用操作锁，运行时数据只能归属已经生效的配置。
    private async Task<SubscriptionProviderSnapshot> CaptureProvidersCoreAsync(CancellationToken cancellationToken)
    {
        var context = _providerContext!;
        var cached = _providerSnapshots.ReadCached(context.SubscriptionId);
        try
        {
            var states = context.Providers.Count == 0
                ? [] : await _providerClient.ReadStatesAsync(cancellationToken).ConfigureAwait(false);
            var snapshot = _providerSnapshots.Capture(context, states, DateTimeOffset.Now);
            if (cached.ContentFingerprint != snapshot.ContentFingerprint || !cached.Providers.SequenceEqual(snapshot.Providers))
            {
                _providerRevision++;
            }
            return snapshot;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or IOException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            AppLogger.Warning($"Provider snapshot refresh failed: {exception.Message}");
            return cached;
        }
    }

    private void SetProviderContext(string? subscriptionId, string runtimeConfig)
    {
        _providerContext = string.IsNullOrWhiteSpace(subscriptionId)
            ? null : _providerSnapshots.CreateContext(subscriptionId, runtimeConfig);
    }
}
