using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Settings;
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop.Services;

internal sealed class WindowAppearanceService : IDisposable
{
    private MainWindow? _window;
    private SettingsThemeViewModel? _theme;
    private bool _isWindowActive;

    public void Attach(MainWindow window, SettingsThemeViewModel theme)
    {
        if (_theme is not null)
        {
            _theme.ThemeChanged -= OnThemeChanged;
            _theme.WindowEffectChanged -= OnWindowEffectChanged;
        }

        if (_window is not null)
        {
            _window.ActualThemeVariantChanged -= OnActualThemeVariantChanged;
            _window.Activated -= OnWindowActivated;
            _window.Deactivated -= OnWindowDeactivated;
        }

        _window = window;
        _theme = theme;
        _isWindowActive = window.IsActive;
        _theme.ThemeChanged += OnThemeChanged;
        _theme.WindowEffectChanged += OnWindowEffectChanged;
        _window.ActualThemeVariantChanged += OnActualThemeVariantChanged;
        _window.Activated += OnWindowActivated;
        _window.Deactivated += OnWindowDeactivated;
        ApplyTheme(theme.SelectedOption.Value);
        ApplyWindowEffect(theme.SelectedWindowEffect);
    }

    public void Reapply()
    {
        if (_theme is null)
        {
            return;
        }

        ApplyTheme(_theme.SelectedOption.Value);
        ApplyWindowEffect(_theme.SelectedWindowEffect);
    }

    public void Dispose()
    {
        if (_theme is not null)
        {
            _theme.ThemeChanged -= OnThemeChanged;
            _theme.WindowEffectChanged -= OnWindowEffectChanged;
        }

        if (_window is not null)
        {
            _window.ActualThemeVariantChanged -= OnActualThemeVariantChanged;
            _window.Activated -= OnWindowActivated;
            _window.Deactivated -= OnWindowDeactivated;
        }

        _theme = null;
        _window = null;
        _isWindowActive = false;
    }

    private void OnThemeChanged(object? sender, AppTheme theme)
    {
        ApplyTheme(theme);
    }

    private void OnWindowEffectChanged(object? sender, WindowEffect effect)
    {
        ApplyWindowEffect(effect);
    }

    private void OnWindowActivated(object? sender, EventArgs args)
    {
        _isWindowActive = true;
        if (OperatingSystem.IsMacOS() && _theme?.SelectedWindowEffect == WindowEffect.Blur)
        {
            UpdateRootSurfaceForCurrentEffect(IsCurrentLightTheme());
        }
    }

    private void OnWindowDeactivated(object? sender, EventArgs args)
    {
        _isWindowActive = false;
        if (OperatingSystem.IsMacOS() && _theme?.SelectedWindowEffect == WindowEffect.Blur)
        {
            UpdateRootSurfaceForCurrentEffect(IsCurrentLightTheme());
        }
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs args)
    {

        if (_theme is null || _theme.SelectedOption.Value != AppTheme.System)
        {
            return;
        }

        UpdateRootSurfaceForCurrentEffect(IsCurrentLightTheme());
    }

    private void ApplyTheme(AppTheme theme)
    {
        if (Avalonia.Application.Current is null)
        {
            return;
        }

        Avalonia.Application.Current.RequestedThemeVariant = theme switch
        {
            AppTheme.Light => Avalonia.Styling.ThemeVariant.Light,
            AppTheme.Dark => Avalonia.Styling.ThemeVariant.Dark,
            _ => Avalonia.Styling.ThemeVariant.Default
        };

        UpdateRootSurfaceForCurrentEffect(IsLightTheme(theme));
    }

    private void ApplyWindowEffect(WindowEffect effect)
    {
        if (_window is null)
        {
            return;
        }

        var transparencyLevel = effect switch
        {
            WindowEffect.Mica => WindowTransparencyLevel.Mica,
            WindowEffect.Acrylic => WindowTransparencyLevel.AcrylicBlur,
            // macOS 后端只把 AcrylicBlur 接到 NSVisualEffectView 模糊。
            WindowEffect.Blur => WindowTransparencyLevel.AcrylicBlur,
            _ => WindowTransparencyLevel.None
        };
        // 同一材质不重新提交，页面重建与热重载只刷新表面资源。
        if (_window.TransparencyLevelHint.Count != 1 || _window.TransparencyLevelHint[0] != transparencyLevel)
        {
            _window.TransparencyLevelHint = [transparencyLevel];
        }

        UpdateRootSurfaceForCurrentEffect(IsCurrentLightTheme());
        AppLogger.Info($"Window effect applied: requested={effect} actual={_window.ActualTransparencyLevel} theme={_window.ActualThemeVariant}");
    }

    private bool IsCurrentLightTheme()
    {
        if (_window is null || _theme is null)
        {
            return false;
        }

        return IsLightTheme(_theme.SelectedOption.Value);
    }

    private bool IsLightTheme(AppTheme theme)
    {
        return theme == AppTheme.Light
            || theme == AppTheme.System && _window?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light;
    }

    private void UpdateRootSurfaceForCurrentEffect(bool isLightTheme)
    {
        if (_window is null || _theme is null)
        {
            return;
        }

        var effect = _theme.SelectedWindowEffect;
        var surfaceBrush = new SolidColorBrush(isLightTheme ? ThemeSurfaceColors.Light : ThemeSurfaceColors.Dark);
        var rootSurfaceBrush = effect switch
        {
            WindowEffect.None => surfaceBrush,
            WindowEffect.Blur when OperatingSystem.IsMacOS() => new SolidColorBrush(Color.Parse(
                isLightTheme
                    ? _isWindowActive ? "#A0FFFFFF" : "#FFEEEEEE"
                    : _isWindowActive ? "#A0000000" : "#FF242424")),
            // Zen 工具栏使用冷灰/近黑底色和 60% 不透明度。
            WindowEffect.Acrylic => new SolidColorBrush(Color.Parse(isLightTheme ? "#99F0F0F4" : "#9917171A")),
            WindowEffect.Blur => new SolidColorBrush(Color.Parse(isLightTheme ? "#B3FFFFFF" : "#B3212121")),
            _ => (IBrush)Brushes.Transparent
        };
        // 这里只处理窗口效果背景；其余样式归 Theme.axaml。
        _window.Resources["AppRootSurfaceBrush"] = rootSurfaceBrush;
        var acrylicCardBrush = effect == WindowEffect.Acrylic && !isLightTheme
            ? new SolidColorBrush(Color.Parse("#CC000000"))
            : null;
        // 深色亚克力卡片使用 80% 不透明度；其他组合移除覆盖，交回主题资源。
        foreach (var resourceKey in new[] { "AppSurfaceBrush", "AppCardBrush", "AppSettingsGroupBrush" })
        {
            if (acrylicCardBrush is not null)
            {
                _window.Resources[resourceKey] = acrylicCardBrush;
            }
            else
            {
                _window.Resources.Remove(resourceKey);
            }
        }

        _window.Resources["AppDialogSurfaceBrush"] = surfaceBrush;
        _window.Resources["AppPopupSurfaceBrush"] = surfaceBrush;
        _window.Resources["ComboBoxPopupBackground"] = surfaceBrush;
        _window.Resources["AppSidebarShadow"] = BoxShadows.Parse(effect == WindowEffect.None
            ? _window.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light
                ? "12 0 28 -18 #24000000, 2 0 8 -4 #22000000"
                : "12 0 28 -18 #32FFFFFF, 2 0 8 -4 #26FFFFFF"
            : isLightTheme
                ? "14 0 34 -16 #3A000000, 3 0 10 -4 #24000000"
                : "14 0 34 -16 #38FFFFFF, 3 0 10 -4 #28FFFFFF");
    }
}
