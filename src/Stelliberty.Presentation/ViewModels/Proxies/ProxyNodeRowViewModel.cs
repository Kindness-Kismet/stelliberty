using Stelliberty.Application.Proxies;
using Stelliberty.Domain.Proxies;
using Stelliberty.Presentation.Proxies;

namespace Stelliberty.Presentation.ViewModels;

public sealed class ProxyNodeRowViewModel : ViewModelBase
{
    private ProxyNode _node;
    private bool _isSelected;
    private bool _isLocated;
    private bool _isClickable;
    private bool _isDelayTesting;

    public ProxyNodeRowViewModel(
        ProxyNode node,
        bool isSelected,
        bool isLocated = false,
        bool isClickable = true,
        bool isDelayTesting = false)
    {
        _node = node;
        _isSelected = isSelected;
        _isLocated = isLocated;
        _isClickable = isClickable;
        _isDelayTesting = isDelayTesting;
    }

    public string Name => _node.Name;
    public string RowAutomationId => $"Proxy.Node.{Name}";
    public string DelayAutomationId => $"Proxy.Node.{Name}.DelayText";
    public string TestDelayAutomationId => $"Proxy.Node.{Name}.TestDelayButton";
    public string DelayTestingAutomationId => $"Proxy.Node.{Name}.DelaySpinner";
    public string SelectAutomationId => $"Proxy.Node.{Name}.SelectButton";

    public string Type => _node.Type;

    public bool IsSelected
    {
        get => _isSelected;
        private set => SetProperty(ref _isSelected, value);
    }

    public bool IsLocated
    {
        get => _isLocated;
        private set => SetProperty(ref _isLocated, value);
    }

    public bool IsClickable
    {
        get => _isClickable;
        private set => SetProperty(ref _isClickable, value);
    }

    public bool IsDelayTesting
    {
        get => _isDelayTesting;
        private set
        {
            if (SetProperty(ref _isDelayTesting, value))
            {
                OnPropertyChanged(nameof(DelayState));
            }
        }
    }

    public string DelayText => _node.Delay switch
    {
        null => "—",
        < 0 => "-1 ms",
        _ => $"{_node.Delay} ms"
    };

    public string DelayState => IsDelayTesting
        ? "testing"
        : _node.Delay switch
        {
            null => "untested",
            < 0 => "failed",
            _ => "tested"
        };

    public string DelayLevel => ProxyDelayPresentation.GetLevel(_node.Delay);

    public void Update(ProxyNode node, bool isSelected, bool isLocated, bool isClickable, bool isDelayTesting)
    {
        if (!ReferenceEquals(_node, node))
        {
            var delayChanged = _node.Delay != node.Delay;
            _node = node;
            if (delayChanged)
            {
                OnPropertyChanged(nameof(DelayText));
                OnPropertyChanged(nameof(DelayState));
                OnPropertyChanged(nameof(DelayLevel));
            }
        }

        IsSelected = isSelected;
        IsLocated = isLocated;
        IsClickable = isClickable;
        IsDelayTesting = isDelayTesting;
    }

    public void ApplyDelay(int delay)
    {
        _node = _node with { Delay = delay };
        OnPropertyChanged(nameof(DelayText));
        OnPropertyChanged(nameof(DelayState));
        OnPropertyChanged(nameof(DelayLevel));
        IsDelayTesting = false;
    }

    internal void SetDelayTesting(bool isDelayTesting) => IsDelayTesting = isDelayTesting;
}
