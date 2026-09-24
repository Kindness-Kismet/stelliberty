using Avalonia.Controls;
using Avalonia.Interactivity;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Desktop.Controls;
using Stelliberty.Desktop.Localization;
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop.Views;

public sealed partial class SubscriptionProviderDialog : UserControl
{
    public SubscriptionProviderDialog()
    {
        InitializeComponent();
    }

    // 文件选择器属于视图，上传与刷新经命令模型处理。
    private async void OnProviderUploadClicked(object? sender, RoutedEventArgs args)
    {
        try
        {
            if (sender is not Button { CommandParameter: string providerName } button
                || DataContext is not SubscriptionProviderViewModel viewModel
                || TopLevel.GetTopLevel(button) is not { } topLevel)
            {
                return;
            }

            var filePath = await LocalFilePicker.PickFileAsync(
                topLevel,
                LocalizationManager.Translate("Subscriptions.FilePicker.Provider.Title"),
                LocalizationManager.Translate("Subscriptions.FilePicker.Provider.Filter"),
                ["*.yaml", "*.yml"]);
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                await viewModel.UploadProviderAsync(providerName, filePath);
            }
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Provider file picker upload failed");
        }
    }
}
