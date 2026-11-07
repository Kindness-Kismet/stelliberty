using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using NavigationPage = Stelliberty.Presentation.ViewModels.NavigationPage;

namespace Stelliberty.Desktop.Views;

public sealed class PageToViewConverter : IValueConverter
{
    private readonly Dictionary<NavigationPage, Control> _views = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not NavigationPage page)
        {
            return null;
        }

        if (parameter is string mode
            && ((string.Equals(mode, "SkipProxy", StringComparison.Ordinal) && page == NavigationPage.Proxy)
                || (string.Equals(mode, "OnlyProxy", StringComparison.Ordinal) && page != NavigationPage.Proxy)))
        {
            return null;
        }

        return GetOrCreateView(page);
    }

    public void ClearCache() => _views.Clear();

    public Control GetOrCreateView(NavigationPage page)
    {
        if (!_views.TryGetValue(page, out var view))
        {
            view = CreateView(page);
            _views[page] = view;
        }

        return view;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static Control CreateView(NavigationPage page) => page switch
    {
        NavigationPage.Home => new HomeView(),
        NavigationPage.Proxy => new ProxyView(),
        NavigationPage.Connections => new ConnectionView(),
        NavigationPage.CoreLogs => new CoreLogView(),
        NavigationPage.Rules => new RuleView(),
        NavigationPage.Subscriptions => new SubscriptionView(),
        NavigationPage.Overrides => new OverrideView(),
        NavigationPage.Settings => new SettingsView(),
        _ => new HomeView()
    };
}
