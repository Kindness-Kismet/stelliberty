using System.Windows.Input;
using Stelliberty.Application.Localization;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Settings;
using Stelliberty.Presentation.Commands;

namespace Stelliberty.Presentation.ViewModels;

public sealed class SettingsThemeViewModel : ViewModelBase, IDisposable
{
    private readonly AppSettings _settings;
    private readonly IAppSettingsStore _settingsStore;
    private readonly ILocalizationService _localization;
    private static readonly IReadOnlyList<WindowEffect> DefaultWindowEffects = [WindowEffect.None, WindowEffect.Mica, WindowEffect.Acrylic];
    private readonly IReadOnlyList<WindowEffect> _windowEffects;
    private AppTheme _selectedTheme;
    private AccentColorMode _accentColorMode;
    private WindowEffect _windowEffect;
    private string _customAccentColor;

    public SettingsThemeViewModel(AppSettings settings, IAppSettingsStore settingsStore, ILocalizationService localization, IWindowEffectCapability? windowEffectCapability = null)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _localization = localization;
        _windowEffects = NormalizeWindowEffects(windowEffectCapability?.SupportedEffects ?? DefaultWindowEffects);
        _selectedTheme = ParseEnum(settings.Theme, AppTheme.System);
        _accentColorMode = ParseEnum(settings.AccentColorMode, AccentColorMode.System);
        _windowEffect = NormalizeWindowEffect(ParseEnum(settings.WindowEffect, WindowEffect.None));
        _customAccentColor = string.IsNullOrWhiteSpace(settings.AccentColor) ? "#3B82F6" : settings.AccentColor;
        settings.AccentColorMode = _accentColorMode.ToString();
        NormalizePersistedWindowEffect();
        _localization.LanguageChanged += OnLanguageChanged;
        EditCustomAccentColorCommand = new RelayCommand(() => CustomAccentRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler<AppTheme>? ThemeChanged;

    public event EventHandler? AccentColorChanged;

    public event EventHandler<WindowEffect>? WindowEffectChanged;

    public event EventHandler? CustomAccentRequested;

    public string FieldLabelText => _localization.GetString("Settings.Field.Theme");

    public string AccentColorSectionTitle => _localization.GetString("Settings.Field.Accent");

    public string WindowEffectSectionTitle => _localization.GetString("Settings.Field.WindowEffect");

    public string SidebarDividerText => _localization.GetString("Settings.Field.SidebarDivider");

    public bool IsSidebarDividerVisible
    {
        get => _settings.IsSidebarDividerVisible;
        set
        {
            if (_settings.IsSidebarDividerVisible == value)
            {
                return;
            }

            _settings.IsSidebarDividerVisible = value;
            _settingsStore.Save(_settings);
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<SelectionOption<AppTheme>> Options =>
    [
        new(AppTheme.System, _localization.GetString("Theme.Option.System")),
        new(AppTheme.Light, _localization.GetString("Theme.Option.Light")),
        new(AppTheme.Dark, _localization.GetString("Theme.Option.Dark")),
    ];

    public SelectionOption<AppTheme> SelectedOption
    {
        get => Options.First(option => option.Value == _selectedTheme);
        set
        {
            if (value.Value == _selectedTheme)
            {
                return;
            }

            _selectedTheme = value.Value;
            _settings.Theme = value.Value.ToString();
            _settingsStore.Save(_settings);
            OnPropertyChanged();
            ThemeChanged?.Invoke(this, value.Value);
        }
    }

    public AccentColorMode AccentMode => _accentColorMode;

    public bool IsCustomAccentMode => _accentColorMode == AccentColorMode.Custom;

    public string CustomAccentColor
    {
        get => _customAccentColor;
        set
        {
            if (string.Equals(_customAccentColor, value, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _customAccentColor = value;
            _settings.AccentColor = value;
            OnPropertyChanged();

            if (_accentColorMode != AccentColorMode.Custom)
            {
                _accentColorMode = AccentColorMode.Custom;
                _settings.AccentColorMode = nameof(AccentColorMode.Custom);
                OnPropertyChanged(nameof(AccentMode));
                OnPropertyChanged(nameof(SelectedAccentModeOption));
                OnPropertyChanged(nameof(IsCustomAccentMode));
            }
            _settingsStore.Save(_settings);
            AccentColorChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public IReadOnlyList<SelectionOption<AccentColorMode>> AccentModeOptions =>
    [
        new(AccentColorMode.System, _localization.GetString("Settings.AccentMode.System")),
        new(AccentColorMode.Custom, _localization.GetString("Settings.AccentMode.Custom")),
    ];

    public SelectionOption<AccentColorMode> SelectedAccentModeOption
    {
        get => AccentModeOptions.First(option => option.Value == _accentColorMode);
        set => SetAccentMode(value.Value);
    }

    public IReadOnlyList<SelectionOption<WindowEffect>> WindowEffectOptions =>
        _windowEffects.Select(effect => new SelectionOption<WindowEffect>(effect, WindowEffectDisplayName(effect))).ToArray();

    public SelectionOption<WindowEffect> SelectedWindowEffectOption
    {
        get => WindowEffectOptions.First(option => option.Value == _windowEffect);
        set => SetWindowEffect(value.Value);
    }

    public WindowEffect SelectedWindowEffect => _windowEffect;

    public bool IsWindowEffectSupported => _windowEffects.Count > 1;

    public ICommand EditCustomAccentColorCommand { get; }

    public void ConfirmCustomAccentColor(string hexColor)
    {
        var colorChanged = !string.Equals(_customAccentColor, hexColor, StringComparison.OrdinalIgnoreCase);
        var wasCustomMode = _accentColorMode == AccentColorMode.Custom;
        _customAccentColor = hexColor;
        _settings.AccentColor = hexColor;
        SetAccentMode(AccentColorMode.Custom);
        OnPropertyChanged(nameof(CustomAccentColor));

        if (colorChanged && wasCustomMode)
        {
            _settingsStore.Save(_settings);
            AccentColorChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void RefreshFromSettings()
    {
        var previousTheme = _selectedTheme;
        var previousAccentMode = _accentColorMode;
        var previousAccentColor = _customAccentColor;
        var previousWindowEffect = _windowEffect;

        _selectedTheme = ParseEnum(_settings.Theme, AppTheme.System);
        _accentColorMode = ParseEnum(_settings.AccentColorMode, AccentColorMode.System);
        _windowEffect = NormalizeWindowEffect(ParseEnum(_settings.WindowEffect, WindowEffect.None));
        _customAccentColor = string.IsNullOrWhiteSpace(_settings.AccentColor) ? "#3B82F6" : _settings.AccentColor;
        _settings.AccentColorMode = _accentColorMode.ToString();
        NormalizePersistedWindowEffect();
        OnPropertyChanged(string.Empty);

        if (previousTheme != _selectedTheme)
        {
            ThemeChanged?.Invoke(this, _selectedTheme);
        }

        if (previousAccentMode != _accentColorMode || !string.Equals(previousAccentColor, _customAccentColor, StringComparison.OrdinalIgnoreCase))
        {
            AccentColorChanged?.Invoke(this, EventArgs.Empty);
        }

        if (previousWindowEffect != _windowEffect)
        {
            WindowEffectChanged?.Invoke(this, _windowEffect);
        }
    }

    public void Dispose()
    {
        _localization.LanguageChanged -= OnLanguageChanged;
    }

    private void SetAccentMode(AccentColorMode mode)
    {
        if (_accentColorMode == mode)
        {
            return;
        }

        _accentColorMode = mode;
        _settings.AccentColorMode = mode.ToString();
        _settingsStore.Save(_settings);
        OnPropertyChanged(nameof(AccentMode));
        OnPropertyChanged(nameof(SelectedAccentModeOption));
        OnPropertyChanged(nameof(IsCustomAccentMode));
        AccentColorChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetWindowEffect(WindowEffect effect)
    {
        if (!_windowEffects.Contains(effect))
        {
            return;
        }

        if (_windowEffect == effect)
        {
            return;
        }

        _windowEffect = effect;
        _settings.WindowEffect = effect.ToString();
        _settingsStore.Save(_settings);
        OnPropertyChanged(nameof(SelectedWindowEffect));
        OnPropertyChanged(nameof(SelectedWindowEffectOption));
        WindowEffectChanged?.Invoke(this, effect);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(FieldLabelText));
        OnPropertyChanged(nameof(AccentColorSectionTitle));
        OnPropertyChanged(nameof(WindowEffectSectionTitle));
        OnPropertyChanged(nameof(SidebarDividerText));
        OnPropertyChanged(nameof(Options));
        OnPropertyChanged(nameof(SelectedOption));
        OnPropertyChanged(nameof(AccentModeOptions));
        OnPropertyChanged(nameof(SelectedAccentModeOption));
        OnPropertyChanged(nameof(WindowEffectOptions));
        OnPropertyChanged(nameof(SelectedWindowEffectOption));
    }

    private static T ParseEnum<T>(string value, T fallback) where T : struct
    {
        return Enum.TryParse<T>(value, out var parsed) ? parsed : fallback;
    }

    private WindowEffect NormalizeWindowEffect(WindowEffect effect)
    {
        return _windowEffects.Contains(effect) ? effect : WindowEffect.None;
    }

    private void NormalizePersistedWindowEffect()
    {
        var normalized = _windowEffect.ToString();
        if (string.Equals(_settings.WindowEffect, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _settings.WindowEffect = normalized;
        _settingsStore.Save(_settings);
    }

    private string WindowEffectDisplayName(WindowEffect effect)
    {
        return _localization.GetString(effect switch
        {
            WindowEffect.Mica => "Settings.WindowEffect.Mica",
            WindowEffect.Acrylic => "Settings.WindowEffect.Acrylic",
            WindowEffect.Blur => "Settings.WindowEffect.Blur",
            _ => "Settings.WindowEffect.None"
        });
    }

    private static IReadOnlyList<WindowEffect> NormalizeWindowEffects(IReadOnlyList<WindowEffect> effects)
    {
        var normalized = new List<WindowEffect>();
        if (!effects.Contains(WindowEffect.None))
        {
            normalized.Add(WindowEffect.None);
        }

        foreach (var effect in effects)
        {
            if (!normalized.Contains(effect))
            {
                normalized.Add(effect);
            }
        }

        return normalized;
    }
}
