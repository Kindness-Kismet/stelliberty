using System.ComponentModel;

namespace Stelliberty.Presentation.ViewModels;

public sealed partial class MainWindowViewModel
{
    private IEnumerable<PageLoadingState> PageLoadingStates =>
        [ProxyPage.Loading, RulePage.Loading, SubscriptionPage.Loading,
            OverridePage.Loading, ConnectionPage.Loading, CoreLogPage.Loading];

    public bool IsCurrentPageLoading => GetPageLoadingState(CurrentPage)?.IsLoading == true;

    public PageLoadingState? GetPageLoadingState(NavigationPage page) => page switch
    {
        NavigationPage.Proxy => ProxyPage.Loading,
        NavigationPage.Rules => RulePage.Loading,
        NavigationPage.Subscriptions => SubscriptionPage.Loading,
        NavigationPage.Overrides => OverridePage.Loading,
        NavigationPage.Connections => ConnectionPage.Loading,
        NavigationPage.CoreLogs => CoreLogPage.Loading,
        _ => null,
    };

    private void OnPageLoadingChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(PageLoadingState.IsLoading)
            && ReferenceEquals(sender, GetPageLoadingState(CurrentPage)))
        {
            OnPropertyChanged(nameof(IsCurrentPageLoading));
        }
    }

    public void CompleteInitialCoreLogLoad()
    {
        List<Stelliberty.Domain.CoreLogs.CoreLogMessage> logs;
        lock (_coreLogLock)
        {
            logs = [.. _pendingCoreLogs];
            _pendingCoreLogs.Clear();
        }
        // 首批日志无需等待追加日志的节流计时。
        CoreLogPage.AppendLogs(logs);
    }
}
