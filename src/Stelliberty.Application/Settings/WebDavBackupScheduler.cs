namespace Stelliberty.Application.Settings;

public sealed class WebDavBackupScheduler(
    IAppSettingsStore settingsStore,
    IWebDavDataBackupService backupService,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task RunDueAsync(CancellationToken cancellationToken)
    {
        var settings = settingsStore.Load();
        if (!settings.IsWebDavBackupEnabled || string.IsNullOrWhiteSpace(settings.WebDavUrl)
            || string.IsNullOrWhiteSpace(settings.WebDavUserName) || string.IsNullOrWhiteSpace(settings.WebDavPassword))
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        if (settings.LastWebDavBackupTime is { } lastBackup
            && now - lastBackup < TimeSpan.FromHours(Math.Max(1, settings.WebDavBackupIntervalHours)))
        {
            return;
        }

        var result = await backupService.CreateBackupAsync(new WebDavBackupSettings(
            settings.WebDavUrl, settings.WebDavRemoteDirectory, settings.WebDavUserName,
            settings.WebDavPassword, Math.Max(1, settings.WebDavBackupRetentionCount)), cancellationToken);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(result.Message);
        }

        settings.LastWebDavBackupTime = now;
        settingsStore.Save(settings);
    }
}
