using System.Windows.Input;
using Stelliberty.Application.Diagnostics;

namespace Stelliberty.Presentation.ViewModels;

public sealed partial class MainWindowViewModel
{
    public NavigationPage CurrentPage
    {
        get => _currentPage;
        set
        {
            if (value != _currentPage)
            {
                long now = System.Environment.TickCount64;
                if (now - _lastNavTickMs < NavThrottleMs) return;
                _lastNavTickMs = now;
            }
            if (SetProperty(ref _currentPage, value))
            {
                RaisePageDependentChanges();
            }
        }
    }

    public bool IsHomeSelected => CurrentPage == NavigationPage.Home;
    public bool IsProxySelected => CurrentPage == NavigationPage.Proxy;
    public bool IsConnectionsSelected => CurrentPage == NavigationPage.Connections;
    public bool IsCoreLogsSelected => CurrentPage == NavigationPage.CoreLogs;
    public bool IsRulesSelected => CurrentPage == NavigationPage.Rules;
    public bool IsSubscriptionsSelected => CurrentPage == NavigationPage.Subscriptions;
    public bool IsOverridesSelected => CurrentPage == NavigationPage.Overrides;
    public bool IsSettingsSelected => CurrentPage == NavigationPage.Settings;

    public ICommand ShowHomeCommand { get; }
    public ICommand ShowProxyCommand { get; }
    public ICommand ShowConnectionsCommand { get; }
    public ICommand ShowCoreLogsCommand { get; }
    public ICommand ShowRulesCommand { get; }
    public ICommand ShowSubscriptionsCommand { get; }
    public ICommand ShowOverridesCommand { get; }
    public ICommand ShowSettingsCommand { get; }

    private void GoToSettingsRoot()
    {
        Settings.GoToRoot();
        CurrentPage = NavigationPage.Settings;
    }

    private void OnSettingsSubPageChanged(object? sender, SettingsSubPage subPage)
    {
        if (subPage == SettingsSubPage.AppLog)
        {
            AppLog.Refresh();
        }
    }

    private void RaisePageDependentChanges()
    {
        OnPropertyChanged(nameof(IsCurrentPageLoading));
        OnPropertyChanged(nameof(IsHomeSelected));
        OnPropertyChanged(nameof(IsProxySelected));
        OnPropertyChanged(nameof(IsConnectionsSelected));
        OnPropertyChanged(nameof(IsCoreLogsSelected));
        OnPropertyChanged(nameof(IsRulesSelected));
        OnPropertyChanged(nameof(IsSubscriptionsSelected));
        OnPropertyChanged(nameof(IsOverridesSelected));
        OnPropertyChanged(nameof(IsSettingsSelected));

        if (_currentPage == NavigationPage.Home)
        {
            HomePage.RefreshNetworkConnection();
        }

        if (_currentPage == NavigationPage.Proxy)
        {
            StartProxySelectionSync();
        }
        else
        {
            StopProxySelectionSync();
        }
    }

    private void StartProxySelectionSync()
    {
        if (_isDisposed)
        {
            return;
        }

        if (_proxySelectionSyncCancellation is { IsCancellationRequested: false })
        {
            return;
        }

        _proxySelectionSyncCancellation = new CancellationTokenSource();
        _ = RunProxySelectionSyncLoopAsync(_proxySelectionSyncCancellation);
    }

    private void StopProxySelectionSync()
    {
        var cancellation = _proxySelectionSyncCancellation;
        if (cancellation is null)
        {
            return;
        }

        _proxySelectionSyncCancellation = null;
        cancellation.Cancel();
    }

    private async Task RunProxySelectionSyncLoopAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await RunProxySelectionSyncOnceAsync(cancellation.Token);
            while (!cancellation.IsCancellationRequested)
            {
                var interval = ProxyPage.IsRuntimeConfigDegraded
                    ? ProxySelectionSyncDegradedInterval
                    : ProxySelectionSyncInterval;
                await Task.Delay(interval, cancellation.Token);
                await RunProxySelectionSyncOnceAsync(cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private Task RunProxySelectionSyncOnceAsync(CancellationToken cancellationToken)
    {
        if (_isDisposed || cancellationToken.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void RunOnUi()
        {
            if (_isDisposed)
            {
                completion.TrySetResult();
                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
                return;
            }

            _ = SyncProxySelectionOnUiAsync(completion, cancellationToken);
        }

        if (_synchronizationContext is not null && SynchronizationContext.Current != _synchronizationContext)
        {
            _synchronizationContext.Post(_ => RunOnUi(), null);
        }
        else
        {
            RunOnUi();
        }

        return completion.Task;
    }

    private async Task SyncProxySelectionOnUiAsync(TaskCompletionSource completion, CancellationToken cancellationToken)
    {
        try
        {
            await ProxyPage.SyncExternalSelectionsAsync(cancellationToken);
            completion.TrySetResult();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            completion.TrySetCanceled(cancellationToken);
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"External proxy selection sync failed: {exception.Message}");
            completion.TrySetResult();
        }
    }
}
