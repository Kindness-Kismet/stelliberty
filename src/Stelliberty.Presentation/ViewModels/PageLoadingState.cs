namespace Stelliberty.Presentation.ViewModels;

// 页面加载在 UI 线程编排；嵌套和重叠请求全部结束后才撤掉遮罩。
public sealed class PageLoadingState : ViewModelBase
{
    private int _activeLoads;

    public bool HasLoaded { get; private set; }
    public bool IsLoading => !HasLoaded || _activeLoads > 0;

    public IDisposable BeginLoading()
    {
        var wasLoading = IsLoading;
        _activeLoads++;
        if (!wasLoading) OnPropertyChanged(nameof(IsLoading));
        return new LoadingScope(this);
    }

    public void CompleteInitialLoad()
    {
        var wasLoading = IsLoading;
        HasLoaded = true;
        if (wasLoading != IsLoading) OnPropertyChanged(nameof(IsLoading));
    }

    private void EndLoading()
    {
        var wasLoading = IsLoading;
        _activeLoads--;
        HasLoaded = true;
        if (wasLoading != IsLoading) OnPropertyChanged(nameof(IsLoading));
    }

    private sealed class LoadingScope(PageLoadingState owner) : IDisposable
    {
        public void Dispose() => owner.EndLoading();
    }
}
