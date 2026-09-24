using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Desktop.Controls;
using Stelliberty.Desktop.Localization;
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop.Views;

public sealed partial class SubscriptionView : UserControl
{
    private readonly GridReorderController _subscriptionReorder;
    private readonly GridReorderController _overrideSelectorReorder;
    private readonly GridReorderController _chainProxyReorder;
    private SubscriptionAddDialogViewModel? _subscribedAddDialog;
    private SubscriptionEditDialogViewModel? _subscribedEditDialog;
    private SubscriptionChainProxyDialogViewModel? _subscribedChainProxyDialog;

    public SubscriptionView()
    {
        InitializeComponent();

        // UserControl DataContext 是 MainWindowViewModel；这里继承页面 VM。
        _subscriptionReorder = new GridReorderController(
            SubscriptionList,
            dataContext => (dataContext as SubscriptionItemViewModel)?.Id,
            container => container,
            (id, targetIndex) => (SubscriptionList.DataContext as SubscriptionPageViewModel)?.MoveSubscriptionCommand
                .Execute(new SubscriptionMoveRequest(id, targetIndex)));
        _overrideSelectorReorder = new GridReorderController(
            OverrideSelectorList,
            dataContext => (dataContext as SubscriptionOverrideOptionViewModel)?.Id,
            container => container,
            (id, targetIndex) => (OverrideSelectorList.DataContext as SubscriptionPageViewModel)?.OverrideSelector.MoveCommand
                .Execute(new SubscriptionOverrideMoveRequest(id, targetIndex)));
        _chainProxyReorder = new GridReorderController(
            ChainProxySlotList,
            dataContext => (dataContext as SubscriptionChainProxySlotViewModel)?.Key,
            container => container.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Classes.Contains("chain-hop")),
            (hopKey, targetIndex) => (SubscriptionPageRoot.DataContext as SubscriptionPageViewModel)?.ChainProxy.MoveDraftNodeCommand
                .Execute(new SubscriptionChainProxyMoveRequest(hopKey, targetIndex)));

        // Overlay 对话框必须沿按钮绑定链解析自己的 VM。
        ChooseLocalFileButton.Command = new Presentation.Commands.RelayCommand(async () => await ChooseLocalFileAsync());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _subscriptionReorder.Attach();
        _overrideSelectorReorder.Attach();
        _chainProxyReorder.Attach();
        SubscribeAddDialog();
        SubscribeEditDialog();
        SubscribeChainProxyDialog();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UnsubscribeAddDialog();
        UnsubscribeEditDialog();
        UnsubscribeChainProxyDialog();
        _subscriptionReorder.Detach();
        _overrideSelectorReorder.Detach();
        _chainProxyReorder.Detach();
        base.OnDetachedFromVisualTree(e);
    }

    private void SubscribeAddDialog()
    {
        if (SubscriptionPageRoot.DataContext is not SubscriptionPageViewModel viewModel
            || ReferenceEquals(_subscribedAddDialog, viewModel.AddDialog))
        {
            return;
        }

        UnsubscribeAddDialog();
        _subscribedAddDialog = viewModel.AddDialog;
        _subscribedAddDialog.DialogStateChanged += OnAddDialogStateChanged;
        _subscribedAddDialog.InputFocusRequested += OnInputFocusRequested;
    }

    private void UnsubscribeAddDialog()
    {
        if (_subscribedAddDialog is null)
        {
            return;
        }

        _subscribedAddDialog.DialogStateChanged -= OnAddDialogStateChanged;
        _subscribedAddDialog.InputFocusRequested -= OnInputFocusRequested;
        _subscribedAddDialog = null;
    }

    private void SubscribeEditDialog()
    {
        if (SubscriptionPageRoot.DataContext is not SubscriptionPageViewModel viewModel
            || ReferenceEquals(_subscribedEditDialog, viewModel.EditDialog))
        {
            return;
        }

        UnsubscribeEditDialog();
        _subscribedEditDialog = viewModel.EditDialog;
        _subscribedEditDialog.DialogStateChanged += OnEditDialogStateChanged;
        _subscribedEditDialog.InputFocusRequested += OnInputFocusRequested;
    }

    private void UnsubscribeEditDialog()
    {
        if (_subscribedEditDialog is null)
        {
            return;
        }

        _subscribedEditDialog.DialogStateChanged -= OnEditDialogStateChanged;
        _subscribedEditDialog.InputFocusRequested -= OnInputFocusRequested;
        _subscribedEditDialog = null;
    }

    private void SubscribeChainProxyDialog()
    {
        if (SubscriptionPageRoot.DataContext is not SubscriptionPageViewModel viewModel
            || ReferenceEquals(_subscribedChainProxyDialog, viewModel.ChainProxy))
        {
            return;
        }

        UnsubscribeChainProxyDialog();
        _subscribedChainProxyDialog = viewModel.ChainProxy;
        _subscribedChainProxyDialog.InputFocusRequested += OnInputFocusRequested;
    }

    private void UnsubscribeChainProxyDialog()
    {
        if (_subscribedChainProxyDialog is null)
        {
            return;
        }

        _subscribedChainProxyDialog.InputFocusRequested -= OnInputFocusRequested;
        _subscribedChainProxyDialog = null;
    }

    private async void OnAddDialogStateChanged(object? sender, EventArgs args)
    {
        await RefreshSubscriptionUrlPasteAvailabilityAsync();
    }

    private async void OnEditDialogStateChanged(object? sender, EventArgs args)
    {
        await RefreshEditSubscriptionUrlPasteAvailabilityAsync();
    }

    private void OnInputFocusRequested(object? sender, DialogInputField field)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var automationId = GetInputAutomationId(sender, field);
            var target = this.GetVisualDescendants()
                .OfType<Control>()
                .FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == automationId);
            target?.BringIntoView();
            target?.Focus();
        }, DispatcherPriority.Input);
    }

    private static string GetInputAutomationId(object? sender, DialogInputField field)
    {
        if (sender is SubscriptionChainProxyDialogViewModel)
        {
            return field switch
            {
                DialogInputField.Name => "Subscriptions.ChainProxy.NameBox",
                DialogInputField.Nodes => "Subscriptions.ChainProxy.SelectedNodesRegion",
                DialogInputField.ProxyGroup => "Subscriptions.ChainProxy.ProxyGroupBox",
                _ => string.Empty,
            };
        }

        var prefix = sender is SubscriptionEditDialogViewModel
            ? "Subscriptions.EditDialog"
            : "Subscriptions.Dialog";
        return field switch
        {
            DialogInputField.Name => $"{prefix}.NameBox",
            DialogInputField.Source => $"{prefix}.UrlBox",
            DialogInputField.LocalFile => "Subscriptions.Dialog.ChooseLocalFileButton",
            DialogInputField.AutoTestDelayInterval => $"{prefix}.AutoTestDelayIntervalBox",
            DialogInputField.AutoUpdateInterval => $"{prefix}.AutoUpdateIntervalBox",
            _ => string.Empty,
        };
    }

    private async void OnSubscriptionUrlBoxGotFocus(object? sender, RoutedEventArgs args)
    {
        await RefreshSubscriptionUrlPasteAvailabilityAsync();
    }

    private async void OnSubscriptionUrlBoxTextChanged(object? sender, TextChangedEventArgs args)
    {
        await RefreshSubscriptionUrlPasteAvailabilityAsync();
    }

    private async void OnPasteSubscriptionUrlClicked(object? sender, RoutedEventArgs args)
    {
        try
        {
            var text = await ReadClipboardTextAsync();
            if (SubscriptionPageRoot.DataContext is SubscriptionPageViewModel viewModel)
            {
                viewModel.AddDialog.PasteUrl(text);
                viewModel.AddDialog.SetClipboardTextAvailable(!string.IsNullOrWhiteSpace(text));
            }
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Subscription URL paste failed: {exception.Message}");
        }
    }

    private async Task RefreshSubscriptionUrlPasteAvailabilityAsync()
    {
        try
        {
            if (SubscriptionPageRoot.DataContext is not SubscriptionPageViewModel viewModel)
            {
                return;
            }

            var text = viewModel.AddDialog.IsUrlPasteButtonVisible ? await ReadClipboardTextAsync() : string.Empty;
            viewModel.AddDialog.SetClipboardTextAvailable(!string.IsNullOrWhiteSpace(text));
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Subscription URL paste state refresh failed: {exception.Message}");
        }
    }

    private async void OnEditSubscriptionUrlBoxGotFocus(object? sender, RoutedEventArgs args)
    {
        await RefreshEditSubscriptionUrlPasteAvailabilityAsync();
    }

    private async void OnEditSubscriptionUrlBoxTextChanged(object? sender, TextChangedEventArgs args)
    {
        await RefreshEditSubscriptionUrlPasteAvailabilityAsync();
    }

    private async void OnPasteEditSubscriptionUrlClicked(object? sender, RoutedEventArgs args)
    {
        try
        {
            var text = await ReadClipboardTextAsync();
            if (SubscriptionPageRoot.DataContext is SubscriptionPageViewModel viewModel)
            {
                viewModel.EditDialog.PasteUrl(text);
                viewModel.EditDialog.SetClipboardTextAvailable(!string.IsNullOrWhiteSpace(text));
            }
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Subscription URL paste failed: {exception.Message}");
        }
    }

    private async Task RefreshEditSubscriptionUrlPasteAvailabilityAsync()
    {
        try
        {
            if (SubscriptionPageRoot.DataContext is not SubscriptionPageViewModel viewModel)
            {
                return;
            }

            var text = viewModel.EditDialog.IsUrlPasteButtonVisible ? await ReadClipboardTextAsync() : string.Empty;
            viewModel.EditDialog.SetClipboardTextAvailable(!string.IsNullOrWhiteSpace(text));
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Subscription URL paste state refresh failed: {exception.Message}");
        }
    }

    private async Task<string> ReadClipboardTextAsync()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        return clipboard is null ? string.Empty : await clipboard.TryGetTextAsync() ?? string.Empty;
    }

    private async Task ChooseLocalFileAsync()
    {
        if (ChooseLocalFileButton.DataContext is not SubscriptionPageViewModel viewModel)
        {
            return;
        }

        if (TopLevel.GetTopLevel(ChooseLocalFileButton) is not { } topLevel)
        {
            return;
        }

        try
        {
            var filePath = await LocalFilePicker.PickFileAsync(
                topLevel,
                Localize("Subscriptions.FilePicker.Subscription.Title"),
                Localize("Subscriptions.FilePicker.Subscription.Filter"),
                ["*.yaml", "*.yml"]);

            if (!string.IsNullOrWhiteSpace(filePath))
            {
                viewModel.AddDialog.LocalFilePath = filePath;
            }
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Subscription file picker failed");
        }
    }

    private void OnAutoTestDelayIntervalBoxGotFocus(object? sender, RoutedEventArgs args)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not SubscriptionPageViewModel viewModel)
        {
            return;
        }

        if (textBox.Name == "AddAutoTestDelayIntervalBox")
        {
            viewModel.AddDialog.BeginAutoTestDelayIntervalEdit();
        }
        else if (textBox.Name == "EditAutoTestDelayIntervalBox")
        {
            viewModel.EditDialog.BeginAutoTestDelayIntervalEdit();
        }
    }

    private void OnAutoTestDelayIntervalBoxLostFocus(object? sender, RoutedEventArgs args)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not SubscriptionPageViewModel viewModel)
        {
            return;
        }

        if (textBox.Name == "AddAutoTestDelayIntervalBox")
        {
            viewModel.AddDialog.EndAutoTestDelayIntervalEdit();
        }
        else if (textBox.Name == "EditAutoTestDelayIntervalBox")
        {
            viewModel.EditDialog.EndAutoTestDelayIntervalEdit();
        }
    }

    private void OnUserAgentBoxGotFocus(object? sender, RoutedEventArgs args)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not SubscriptionPageViewModel viewModel)
        {
            return;
        }

        if (textBox.Name == "AddUserAgentBox")
        {
            viewModel.AddDialog.BeginUserAgentEdit();
        }
        else if (textBox.Name == "EditUserAgentBox")
        {
            viewModel.EditDialog.BeginUserAgentEdit();
        }
    }

    private void OnUserAgentBoxLostFocus(object? sender, RoutedEventArgs args)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not SubscriptionPageViewModel viewModel)
        {
            return;
        }

        if (textBox.Name == "AddUserAgentBox")
        {
            viewModel.AddDialog.EndUserAgentEdit();
        }
        else if (textBox.Name == "EditUserAgentBox")
        {
            viewModel.EditDialog.EndUserAgentEdit();
        }
    }

    private static string Localize(string key) => LocalizationManager.Translate(key);
}
