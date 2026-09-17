using Stelliberty.Application.Settings;

namespace Stelliberty.Presentation.ViewModels;

public sealed partial class SettingsCoreConfigViewModel
{
    public string SystemTunText => _localization.GetString("Settings.System.Tun");
    public string SystemTunStackText => _localization.GetString("Settings.System.TunStack");
    public string SystemTunDeviceText => _localization.GetString("Settings.System.TunDevice");
    public string SystemTunAutoRouteText => _localization.GetString("Settings.System.TunAutoRoute");
    public string SystemTunAutoRedirectText => _localization.GetString("Settings.System.TunAutoRedirect");
    public string SystemTunAutoDetectInterfaceText => _localization.GetString("Settings.System.TunAutoDetectInterface");
    public string SystemTunStrictRouteText => _localization.GetString("Settings.System.TunStrictRoute");
    public string SystemTunDnsHijackText => _localization.GetString("Settings.System.TunDnsHijack");
    public string SystemTunRouteExcludeText => _localization.GetString("Settings.System.TunRouteExclude");
    public string SystemTunDisableIcmpForwardingText => _localization.GetString("Settings.System.TunDisableIcmpForwarding");
    public string SystemTunMtuText => _localization.GetString("Settings.System.TunMtu");
    public string SystemTunMtuDescriptionText => _localization.GetString("Settings.System.TunMtuDescription");

    public IReadOnlyList<string> TunStackOptions { get; } = ["Mixed", "System", "gVisor", "Mips"];

    public string? SelectedTunStack
    {
        // 协议栈匹配遵循核心不区分大小写的规则。
        get => TunStackOptions.FirstOrDefault(option => string.Equals(option, _settings.TunStack.Trim(), StringComparison.OrdinalIgnoreCase));
        set
        {
            if (value is not null)
            {
                TunStack = value;
            }
        }
    }

    public bool IsTunEnabled
    {
        get => _settings.IsTunEnabled;
        set => SetTunEnabled(value);
    }

    public string TunStack
    {
        get => _settings.TunStack;
        set
        {
            if (SetTrimmedStringWithArea(_settings.TunStack, value, next => _settings.TunStack = next, "Tun"))
            {
                OnPropertyChanged(nameof(SelectedTunStack));
            }
        }
    }

    public string TunDevice
    {
        get => _settings.TunDevice;
        set => SetWithArea(_settings.TunDevice, value, next => _settings.TunDevice = next, "Tun");
    }

    public bool IsTunAutoRouteEnabled
    {
        get => _settings.IsTunAutoRouteEnabled;
        set => SetWithArea(_settings.IsTunAutoRouteEnabled, value, next => _settings.IsTunAutoRouteEnabled = next, "Tun");
    }

    public bool IsTunAutoRedirectEnabled
    {
        get => _settings.IsTunAutoRedirectEnabled;
        set => SetWithArea(_settings.IsTunAutoRedirectEnabled, value, next => _settings.IsTunAutoRedirectEnabled = next, "Tun");
    }

    public bool IsTunAutoDetectInterfaceEnabled
    {
        get => _settings.IsTunAutoDetectInterfaceEnabled;
        set => SetWithArea(_settings.IsTunAutoDetectInterfaceEnabled, value, next => _settings.IsTunAutoDetectInterfaceEnabled = next, "Tun");
    }

    public bool IsTunStrictRouteEnabled
    {
        get => _settings.IsTunStrictRouteEnabled;
        set => SetWithArea(_settings.IsTunStrictRouteEnabled, value, next => _settings.IsTunStrictRouteEnabled = next, "Tun");
    }

    public string TunDnsHijackText
    {
        get => string.Join(Environment.NewLine, _settings.TunDnsHijack);
        set => SetStringListWithArea(_settings.TunDnsHijack, value, next => _settings.TunDnsHijack = next, "Tun");
    }

    public string TunRouteExcludeAddressesText
    {
        get => string.Join(Environment.NewLine, _settings.TunRouteExcludeAddresses);
        set => SetStringListWithArea(_settings.TunRouteExcludeAddresses, value, next => _settings.TunRouteExcludeAddresses = next, "Tun");
    }

    public bool IsTunIcmpForwardingDisabled
    {
        get => _settings.IsTunIcmpForwardingDisabled;
        set => SetWithArea(_settings.IsTunIcmpForwardingDisabled, value, next => _settings.IsTunIcmpForwardingDisabled = next, "Tun");
    }

    public string TunMtuText
    {
        get => _tunMtuText;
        set => SetNullableIntWithArea(_settings.TunMtu, value, AppSettings.DefaultTunMtu.ToString(), next => _settings.TunMtu = next, "Tun", next => _tunMtuText = next);
    }

    public void ApplyTunFromHome(bool isEnabled)
    {
        IsTunEnabled = isEnabled;
    }

    private void SetTunEnabled(bool isEnabled)
    {
        SetWithArea(_settings.IsTunEnabled, isEnabled, next => _settings.IsTunEnabled = next, "Tun");
        _applyTunStateToHome(_settings.IsTunEnabled);
    }
}
