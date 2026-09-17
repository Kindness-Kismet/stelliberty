#if DEBUG
using Stelliberty.Application.Localization;
using Stelliberty.Application.Settings;
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop.Debug;

internal static partial class DebugCommands
{
    private static Task<string?> ExecuteSettingsCommandAsync(MainWindow window, string command)
    {
        var viewModel = RequireViewModel(window);
        var spec = command["settings.".Length..].Trim();
        if (spec.StartsWith("language.", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<string?>(ExecuteLanguageSettingsCommand(window, viewModel, spec["language.".Length..].Trim()));
        }

        if (spec.StartsWith("theme.", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<string?>(ExecuteThemeSettingsCommand(viewModel, spec["theme.".Length..].Trim()));
        }

        if (spec.StartsWith("accent.", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<string?>(ExecuteAccentSettingsCommand(viewModel, spec["accent.".Length..].Trim()));
        }

        if (spec.StartsWith("window-effect.", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<string?>(ExecuteWindowEffectSettingsCommand(viewModel, spec["window-effect.".Length..].Trim()));
        }

        if (spec.StartsWith("app-behavior.", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteAppBehaviorSettingsCommandAsync(viewModel, spec["app-behavior.".Length..].Trim());
        }

        if (spec.StartsWith("update.", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteUpdateSettingsCommandAsync(viewModel, spec["update.".Length..].Trim());
        }

        if (spec.StartsWith("data-management.", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteDataManagementSettingsCommandAsync(viewModel, spec["data-management.".Length..].Trim());
        }

        if (spec.StartsWith("app-log.", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteAppLogSettingsCommandAsync(viewModel, spec["app-log.".Length..].Trim());
        }

        if (spec.StartsWith("system-integration.", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<string?>(ExecuteSystemIntegrationSettingsCommand(viewModel, spec["system-integration.".Length..].Trim()));
        }

        if (string.Equals(spec, "state", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<string?>(SettingsState(viewModel));
        }

        throw new InvalidOperationException($"Unknown settings command: {command}");
    }

    private static string SettingsState(MainWindowViewModel viewModel)
    {
        return string.Join(";", [
            $"language={viewModel.Language.SelectedOption.Value}",
            $"theme={viewModel.Theme.SelectedOption.Value}",
            $"accentMode={viewModel.Theme.AccentMode}",
            $"accentColor={viewModel.Theme.CustomAccentColor}",
            $"windowEffect={viewModel.Theme.SelectedWindowEffect}",
            $"silentStart={Bool(viewModel.AppBehavior.IsSilentStartEnabled)}",
            $"minimizeToTray={Bool(viewModel.AppBehavior.IsMinimizeToTrayEnabled)}",
            $"lightweightMode={Bool(viewModel.AppBehavior.IsLightweightModeEnabled)}",
            $"trayDoubleClick={Bool(viewModel.AppBehavior.IsTrayDoubleClickEnabled)}",
            $"lazyMode={Bool(viewModel.AppBehavior.IsLazyModeEnabled)}",
            $"titleBarFps={Bool(viewModel.AppBehavior.IsTitleBarFpsVisible)}",
            $"autoStart={Bool(viewModel.AppBehavior.IsAutoStartEnabled)}",
            $"autoCheck={Bool(viewModel.Update.IsAutoCheckEnabled)}",
            $"updateInterval={viewModel.Update.SelectedCheckIntervalOption.Value}",
            $"proxyHost={viewModel.SystemIntegration.ProxyHost}",
            $"pacMode={Bool(viewModel.SystemIntegration.IsPacModeEnabled)}"
        ]);
    }

    private static string ThemeState(MainWindowViewModel viewModel)
    {
        return string.Join(";", [
            $"theme={viewModel.Theme.SelectedOption.Value}",
            $"accentMode={viewModel.Theme.AccentMode}",
            $"accentColor={viewModel.Theme.CustomAccentColor}",
            $"windowEffect={viewModel.Theme.SelectedWindowEffect}",
            $"windowEffectSupported={Bool(viewModel.Theme.IsWindowEffectSupported)}"
        ]);
    }

    private static string AppBehaviorSettingsState(MainWindowViewModel viewModel)
    {
        var behavior = viewModel.AppBehavior;
        return string.Join(";", [
            $"silentStart={Bool(behavior.IsSilentStartEnabled)}",
            $"minimizeToTray={Bool(behavior.IsMinimizeToTrayEnabled)}",
            $"lightweightMode={Bool(behavior.IsLightweightModeEnabled)}",
            $"trayDoubleClick={Bool(behavior.IsTrayDoubleClickEnabled)}",
            $"lazyMode={Bool(behavior.IsLazyModeEnabled)}",
            $"titleBarFps={Bool(behavior.IsTitleBarFpsVisible)}",
            $"autoStart={Bool(behavior.IsAutoStartEnabled)}",
            $"windowToggleHotkey={behavior.WindowToggleHotkey}",
            $"systemProxyToggleHotkey={behavior.SystemProxyToggleHotkey}",
            $"tunToggleHotkey={behavior.TunToggleHotkey}"
        ]);
    }

    private static string UpdateSettingsState(MainWindowViewModel viewModel)
    {
        var update = viewModel.Update;
        return string.Join(";", [
            $"autoCheck={Bool(update.IsAutoCheckEnabled)}",
            $"interval={update.SelectedCheckIntervalOption.Value}",
            $"channel={update.SelectedChannelOption.Value}",
            $"lastOperation={update.LastOperation}",
            $"lastCheck={update.LastCheckText}",
            $"latest={update.LatestVersionText}",
            $"releaseUrl={update.LatestReleaseUrl}",
            $"ignored={update.IgnoredVersionText}",
            $"canIgnore={Bool(update.CanIgnoreLatestVersion)}",
            $"canOpen={Bool(update.CanOpenLatestRelease)}",
            $"status={update.StatusText}"
        ]);
    }

    private static string DataManagementSettingsState(MainWindowViewModel viewModel)
    {
        var data = viewModel.DataManagement;
        return string.Join(";", [
            $"lastOperation={data.LastOperation}",
            $"restoreDialog={Bool(data.IsRestoreDialogVisible)}",
            $"restoreMode={data.SelectedRestoreMode}",
            $"webdavEnabled={Bool(data.IsWebDavBackupEnabled)}",
            $"webdavBusy={Bool(data.IsWebDavBusy)}",
            $"webdavUrlSet={Bool(!string.IsNullOrWhiteSpace(data.WebDavUrl))}",
            $"webdavUserSet={Bool(!string.IsNullOrWhiteSpace(data.WebDavUserName))}",
            $"webdavRemoteDirectory={data.WebDavRemoteDirectory}",
            $"webdavIntervalHours={data.WebDavBackupIntervalHoursText}",
            $"webdavRetentionCount={data.WebDavBackupRetentionCountText}",
            $"webdavDialog={Bool(data.IsWebDavBackupDialogVisible)}",
            $"webdavDialogBusy={Bool(data.IsWebDavBackupDialogBusy)}",
            $"webdavBackupCount={data.WebDavBackupItems.Count}",
            $"webdavStatus={data.WebDavStatusText}"
        ]);
    }

    private static string AppLogSettingsState(MainWindowViewModel viewModel)
    {
        var appLog = viewModel.AppLog;
        return string.Join(";", [
            $"total={appLog.TotalLogCount}",
            $"filtered={appLog.FilteredLogCount}",
            $"warnings={appLog.WarningLogCount}",
            $"errors={appLog.ErrorLogCount}",
            $"loading={Bool(appLog.IsLoading)}",
            $"empty={Bool(appLog.IsEmptyVisible)}",
            $"status={appLog.StatusText}"
        ]);
    }

    private static string SystemIntegrationSettingsState(MainWindowViewModel viewModel)
    {
        var system = viewModel.SystemIntegration;
        return string.Join(";", [
            $"proxyHost={system.ProxyHost}",
            $"bypass={system.SystemProxyBypass}",
            $"pacMode={Bool(system.IsPacModeEnabled)}",
            $"pacScript={system.PacScript}",
            $"areas={string.Join(',', system.ChangeAreas)}",
            $"candidates={string.Join(',', system.SystemProxyHostCandidates)}",
            $"uwpDialog={Bool(system.IsUwpLoopbackDialogVisible)}",
            $"uwpStatus={system.UwpLoopbackStatusText}",
            $"uwpSelected={system.AllUwpItems.Count(item => item.IsSelected)}"
        ]);
    }

    private static string FormatUwpItems(IReadOnlyList<UwpLoopbackItemViewModel> items)
    {
        return string.Join("|", items.Select(item =>
            $"{item.PackageFamilyName}\t{item.DisplayName}\tac={item.AppContainerName}\tsid={item.Sid}\tselected={Bool(item.IsSelected)}"));
    }

    private static string FormatWebDavBackupItems(IReadOnlyList<WebDavBackupItemViewModel> items)
    {
        return string.Join("|", items.Select(item =>
            $"{item.Id}\t{item.DisplayName}\t{item.DetailText}\tbusy={Bool(item.IsBusy)}"));
    }

    private static DataRestoreMode ParseRestoreMode(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "overwrite" => DataRestoreMode.Overwrite,
            "merge" => DataRestoreMode.Merge,
            _ => throw new InvalidOperationException($"Unknown restore mode: {value}")
        };
    }

    private static void SelectRestoreMode(SettingsDataManagementViewModel data, DataRestoreMode mode)
    {
        if (mode == DataRestoreMode.Overwrite)
        {
            data.SelectOverwriteModeCommand.Execute(null);
            return;
        }

        data.SelectMergeModeCommand.Execute(null);
    }

    private static string ExecuteLanguageSettingsCommand(MainWindow window, MainWindowViewModel viewModel, string spec)
    {
        if (string.Equals(spec, "state", StringComparison.OrdinalIgnoreCase))
        {
            return $"language={viewModel.Language.SelectedOption.Value}";
        }

        if (spec.StartsWith("set ", StringComparison.OrdinalIgnoreCase))
        {
            SwitchLanguage(window, spec["set ".Length..].Trim());
            return $"language={viewModel.Language.SelectedOption.Value}";
        }

        throw new InvalidOperationException($"Unknown language settings command: settings.language.{spec}");
    }

    private static void SwitchLanguage(MainWindow window, string spec)
    {
        if (window.DataContext is not MainWindowViewModel viewModel)
        {
            throw new InvalidOperationException("DataContext is not ready");
        }

        var language = spec.ToLowerInvariant() switch
        {
            "zh" or "zh-hans" or "zhhans" => AppLanguage.ZhHans,
            "zh-hant" or "zhhant" or "zh-tw" or "zhtw" => AppLanguage.ZhHant,
            "en" => AppLanguage.En,
            "sys" or "system" => AppLanguage.System,
            _ => throw new InvalidOperationException($"Unknown language: {spec}")
        };
        viewModel.Language.SetLanguage(language);
    }

    private static string ExecuteThemeSettingsCommand(MainWindowViewModel viewModel, string spec)
    {
        if (string.Equals(spec, "state", StringComparison.OrdinalIgnoreCase))
        {
            return ThemeState(viewModel);
        }

        if (spec.StartsWith("set ", StringComparison.OrdinalIgnoreCase))
        {
            var theme = ParseAppTheme(spec["set ".Length..].Trim());
            viewModel.Theme.SelectedOption = viewModel.Theme.Options.First(option => option.Value == theme);
            return ThemeState(viewModel);
        }

        throw new InvalidOperationException($"Unknown theme settings command: settings.theme.{spec}");
    }

    private static string ExecuteAccentSettingsCommand(MainWindowViewModel viewModel, string spec)
    {
        if (spec.StartsWith("set mode ", StringComparison.OrdinalIgnoreCase))
        {
            var mode = ParseAccentColorMode(spec["set mode ".Length..].Trim());
            viewModel.Theme.SelectedAccentModeOption = viewModel.Theme.AccentModeOptions.First(option => option.Value == mode);
            return ThemeState(viewModel);
        }

        if (spec.StartsWith("set color ", StringComparison.OrdinalIgnoreCase))
        {
            viewModel.Theme.ConfirmCustomAccentColor(NormalizeInputValue(spec["set color ".Length..].Trim()));
            return ThemeState(viewModel);
        }

        throw new InvalidOperationException($"Unknown accent settings command: settings.accent.{spec}");
    }

    private static string ExecuteWindowEffectSettingsCommand(MainWindowViewModel viewModel, string spec)
    {
        if (spec.StartsWith("set ", StringComparison.OrdinalIgnoreCase))
        {
            var effect = ParseWindowEffect(spec["set ".Length..].Trim());
            viewModel.Theme.SelectedWindowEffectOption = viewModel.Theme.WindowEffectOptions.First(option => option.Value == effect);
            return ThemeState(viewModel);
        }

        throw new InvalidOperationException($"Unknown window effect settings command: settings.window-effect.{spec}");
    }

    private static async Task<string?> ExecuteAppBehaviorSettingsCommandAsync(
        MainWindowViewModel viewModel,
        string spec)
    {
        if (string.Equals(spec, "list keys", StringComparison.OrdinalIgnoreCase))
        {
            return string.Join("|", AppBehaviorSettingKeys());
        }

        if (string.Equals(spec, "state", StringComparison.OrdinalIgnoreCase))
        {
            return AppBehaviorSettingsState(viewModel);
        }

        if (spec.StartsWith("set ", StringComparison.OrdinalIgnoreCase))
        {
            var parts = SplitCommandTokens(spec["set ".Length..]);
            if (parts.Count < 2)
            {
                throw new InvalidOperationException("settings.app-behavior.set usage: settings.app-behavior.set <key> <value>");
            }

            await SetAppBehaviorSettingAsync(viewModel, parts[0], parts[1]);
            return AppBehaviorSettingsState(viewModel);
        }

        throw new InvalidOperationException($"Unknown app behavior settings command: settings.app-behavior.{spec}");
    }

    private static async Task<string?> ExecuteUpdateSettingsCommandAsync(MainWindowViewModel viewModel, string spec)
    {
        var update = viewModel.Update;
        if (string.Equals(spec, "state", StringComparison.OrdinalIgnoreCase))
        {
            return UpdateSettingsState(viewModel);
        }

        if (spec.StartsWith("set auto-check ", StringComparison.OrdinalIgnoreCase))
        {
            update.IsAutoCheckEnabled = ParseBool(spec["set auto-check ".Length..].Trim());
            return UpdateSettingsState(viewModel);
        }

        if (spec.StartsWith("set interval ", StringComparison.OrdinalIgnoreCase))
        {
            var value = spec["set interval ".Length..].Trim();
            update.SelectedCheckIntervalOption = update.CheckIntervalOptions.First(option => string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase));
            return UpdateSettingsState(viewModel);
        }

        if (spec.StartsWith("set channel ", StringComparison.OrdinalIgnoreCase))
        {
            var value = spec["set channel ".Length..].Trim();
            update.SelectedChannelOption = update.ChannelOptions.First(option => string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase));
            return UpdateSettingsState(viewModel);
        }

        if (string.Equals(spec, "check", StringComparison.OrdinalIgnoreCase))
        {
            await update.CheckAsync();
            return UpdateSettingsState(viewModel);
        }

        if (string.Equals(spec, "ignore latest", StringComparison.OrdinalIgnoreCase))
        {
            update.IgnoreLatestVersionCommand.Execute(null);
            return UpdateSettingsState(viewModel);
        }

        throw new InvalidOperationException($"Unknown update settings command: settings.update.{spec}");
    }

    private static async Task<string?> ExecuteDataManagementSettingsCommandAsync(MainWindowViewModel viewModel, string spec)
    {
        var data = viewModel.DataManagement;
        if (string.Equals(spec, "state", StringComparison.OrdinalIgnoreCase))
        {
            return DataManagementSettingsState(viewModel);
        }

        if (string.Equals(spec, "backup", StringComparison.OrdinalIgnoreCase) || spec.StartsWith("backup ", StringComparison.OrdinalIgnoreCase))
        {
            var tokens = spec.Length > "backup".Length ? SplitCommandTokens(spec["backup ".Length..].Trim()) : [];
            if (tokens.Count != 1)
            {
                throw new InvalidOperationException("settings.data-management.backup usage: settings.data-management.backup <path>");
            }

            var backupPath = NormalizeInputValue(tokens[0]);
            if (string.IsNullOrWhiteSpace(backupPath))
            {
                throw new InvalidOperationException("settings.data-management.backup usage: settings.data-management.backup <path>");
            }

            data.CreateBackupToFile(backupPath);
            return DataManagementSettingsState(viewModel);
        }

        if (string.Equals(spec, "restore", StringComparison.OrdinalIgnoreCase) || spec.StartsWith("restore ", StringComparison.OrdinalIgnoreCase))
        {
            var tokens = spec.Length > "restore".Length ? SplitCommandTokens(spec["restore ".Length..].Trim()) : [];
            if (tokens.Count != 2)
            {
                throw new InvalidOperationException("settings.data-management.restore usage: settings.data-management.restore <path> <overwrite|merge>");
            }

            var backupPath = NormalizeInputValue(tokens[0]);
            if (string.IsNullOrWhiteSpace(backupPath))
            {
                throw new InvalidOperationException("settings.data-management.restore usage: settings.data-management.restore <path> <overwrite|merge>");
            }

            var mode = ParseRestoreMode(tokens[1]);
            data.BeginRestoreFromFile(backupPath);
            SelectRestoreMode(data, mode);
            await data.ConfirmRestoreAsync();
            return DataManagementSettingsState(viewModel);
        }

        if (string.Equals(spec, "webdav.list keys", StringComparison.OrdinalIgnoreCase))
        {
            return string.Join("|", WebDavSettingKeys());
        }

        if (string.Equals(spec, "webdav.state", StringComparison.OrdinalIgnoreCase))
        {
            return DataManagementSettingsState(viewModel);
        }

        if (spec.StartsWith("webdav.set ", StringComparison.OrdinalIgnoreCase))
        {
            var parts = SplitCommandTokens(spec["webdav.set ".Length..]);
            if (parts.Count < 2)
            {
                throw new InvalidOperationException("settings.data-management.webdav.set usage: settings.data-management.webdav.set <key> <value>");
            }

            SetWebDavSetting(data, parts[0], parts[1]);
            return DataManagementSettingsState(viewModel);
        }

        if (string.Equals(spec, "webdav.test", StringComparison.OrdinalIgnoreCase))
        {
            await data.TestWebDavConnectionAsync();
            return DataManagementSettingsState(viewModel);
        }

        if (string.Equals(spec, "webdav.backup", StringComparison.OrdinalIgnoreCase))
        {
            await data.CreateWebDavBackupAsync();
            return DataManagementSettingsState(viewModel);
        }

        if (string.Equals(spec, "webdav.open dialog", StringComparison.OrdinalIgnoreCase))
        {
            await data.OpenWebDavBackupDialogAsync();
            return DataManagementSettingsState(viewModel);
        }

        if (string.Equals(spec, "webdav.list", StringComparison.OrdinalIgnoreCase))
        {
            await data.RefreshWebDavBackupsAsync();
            return FormatWebDavBackupItems(data.WebDavBackupItems);
        }

        if (spec.StartsWith("webdav.restore backup ", StringComparison.OrdinalIgnoreCase))
        {
            var fileName = NormalizeInputValue(spec["webdav.restore backup ".Length..].Trim());
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new InvalidOperationException("settings.data-management.webdav.restore backup usage: settings.data-management.webdav.restore backup <fileName>");
            }

            await data.RestoreWebDavBackupAsync(fileName);
            return DataManagementSettingsState(viewModel);
        }

        if (spec.StartsWith("webdav.delete backup ", StringComparison.OrdinalIgnoreCase))
        {
            var fileName = NormalizeInputValue(spec["webdav.delete backup ".Length..].Trim());
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new InvalidOperationException("settings.data-management.webdav.delete backup usage: settings.data-management.webdav.delete backup <fileName>");
            }

            await data.DeleteWebDavBackupAsync(fileName);
            return FormatWebDavBackupItems(data.WebDavBackupItems);
        }

        if (string.Equals(spec, "webdav.restore", StringComparison.OrdinalIgnoreCase)
            || spec.StartsWith("webdav.restore ", StringComparison.OrdinalIgnoreCase))
        {
            var tokens = spec.Length > "webdav.restore".Length ? SplitCommandTokens(spec["webdav.restore ".Length..].Trim()) : [];
            if (tokens.Count != 1)
            {
                throw new InvalidOperationException("settings.data-management.webdav.restore usage: settings.data-management.webdav.restore <overwrite|merge>");
            }

            data.ShowWebDavRestoreLatestDialogCommand.Execute(null);
            SelectRestoreMode(data, ParseRestoreMode(tokens[0]));
            await data.ConfirmRestoreAsync();
            return DataManagementSettingsState(viewModel);
        }

        throw new InvalidOperationException($"Unknown data management settings command: settings.data-management.{spec}");
    }

    private static IReadOnlyList<string> WebDavSettingKeys()
    {
        return
        [
            "enabled",
            "url",
            "username",
            "password",
            "remote-directory",
            "interval-hours",
            "retention-count"
        ];
    }

    private static void SetWebDavSetting(SettingsDataManagementViewModel data, string key, string value)
    {
        var normalizedValue = NormalizeInputValue(value);
        switch (key.ToLowerInvariant())
        {
            case "enabled":
                data.IsWebDavBackupEnabled = ParseBool(normalizedValue);
                break;
            case "url":
                data.WebDavUrl = normalizedValue;
                break;
            case "username":
                data.WebDavUserName = normalizedValue;
                break;
            case "password":
                data.WebDavPassword = normalizedValue;
                break;
            case "remote-directory":
                data.WebDavRemoteDirectory = normalizedValue;
                break;
            case "interval-hours":
                data.WebDavBackupIntervalHoursText = normalizedValue;
                break;
            case "retention-count":
                data.WebDavBackupRetentionCountText = normalizedValue;
                break;
            default:
                throw new InvalidOperationException($"Unknown WebDAV setting key: {key}");
        }
    }

    private static async Task<string?> ExecuteAppLogSettingsCommandAsync(MainWindowViewModel viewModel, string spec)
    {
        var appLog = viewModel.AppLog;
        if (string.Equals(spec, "state", StringComparison.OrdinalIgnoreCase))
        {
            return AppLogSettingsState(viewModel);
        }

        if (string.Equals(spec, "refresh", StringComparison.OrdinalIgnoreCase))
        {
            await appLog.RefreshAsync();
            return AppLogSettingsState(viewModel);
        }

        if (string.Equals(spec, "export", StringComparison.OrdinalIgnoreCase) || spec.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
        {
            var tokens = spec.Length > "export".Length ? SplitCommandTokens(spec["export ".Length..].Trim()) : [];
            if (tokens.Count != 1)
            {
                throw new InvalidOperationException("settings.app-log.export usage: settings.app-log.export <path>");
            }

            var exportPath = NormalizeInputValue(tokens[0]);
            if (string.IsNullOrWhiteSpace(exportPath))
            {
                throw new InvalidOperationException("settings.app-log.export usage: settings.app-log.export <path>");
            }

            await appLog.ExportToFileAsync(exportPath);
            return AppLogSettingsState(viewModel);
        }

        throw new InvalidOperationException($"Unknown app log settings command: settings.app-log.{spec}");
    }

    private static string ExecuteSystemIntegrationSettingsCommand(MainWindowViewModel viewModel, string spec)
    {
        var system = viewModel.SystemIntegration;
        if (string.Equals(spec, "list keys", StringComparison.OrdinalIgnoreCase))
        {
            return string.Join("|", SystemIntegrationSettingKeys());
        }

        if (string.Equals(spec, "state", StringComparison.OrdinalIgnoreCase))
        {
            return SystemIntegrationSettingsState(viewModel);
        }

        if (spec.StartsWith("set ", StringComparison.OrdinalIgnoreCase))
        {
            var parts = SplitCommandTokens(spec["set ".Length..]);
            if (parts.Count < 2)
            {
                throw new InvalidOperationException("settings.system-integration.set usage: settings.system-integration.set <key> <value>");
            }

            SetSystemIntegrationSetting(viewModel, parts[0], parts[1]);
            return SystemIntegrationSettingsState(viewModel);
        }

        if (string.Equals(spec, "refresh proxy-host", StringComparison.OrdinalIgnoreCase))
        {
            system.RefreshSystemProxyHostCandidatesCommand.Execute(null);
            return SystemIntegrationSettingsState(viewModel);
        }

        if (string.Equals(spec, "uwp.list", StringComparison.OrdinalIgnoreCase))
        {
            system.ShowUwpLoopbackDialogCommand.Execute(null);
            return FormatUwpItems(system.AllUwpItems);
        }

        if (spec.StartsWith("uwp.search ", StringComparison.OrdinalIgnoreCase))
        {
            system.UwpSearchText = NormalizeInputValue(spec["uwp.search ".Length..].Trim());
            return FormatUwpItems(system.UwpLoopbackItems);
        }

        if (string.Equals(spec, "uwp.select all", StringComparison.OrdinalIgnoreCase))
        {
            system.SelectAllUwpCommand.Execute(null);
            return FormatUwpItems(system.UwpLoopbackItems);
        }

        if (string.Equals(spec, "uwp.invert", StringComparison.OrdinalIgnoreCase))
        {
            system.InvertUwpSelectionCommand.Execute(null);
            return FormatUwpItems(system.UwpLoopbackItems);
        }

        if (spec.StartsWith("uwp.set ", StringComparison.OrdinalIgnoreCase))
        {
            var parts = SplitCommandTokens(spec["uwp.set ".Length..]);
            if (parts.Count < 2)
            {
                throw new InvalidOperationException("settings.system-integration.uwp.set usage: settings.system-integration.uwp.set <package_family_name> <true|false>");
            }

            if (!system.SetUwpItemSelected(parts[0], ParseBool(parts[1])))
            {
                throw new InvalidOperationException($"UWP package not found: {parts[0]}");
            }

            return FormatUwpItems(system.AllUwpItems);
        }

        if (string.Equals(spec, "uwp.save", StringComparison.OrdinalIgnoreCase))
        {
            system.SaveUwpLoopbackCommand.Execute(null);
            return SystemIntegrationSettingsState(viewModel);
        }

        if (string.Equals(spec, "uwp.cancel", StringComparison.OrdinalIgnoreCase))
        {
            system.CloseUwpLoopbackDialogCommand.Execute(null);
            return SystemIntegrationSettingsState(viewModel);
        }

        throw new InvalidOperationException($"Unknown system integration settings command: settings.system-integration.{spec}");
    }

    private static IReadOnlyList<string> AppBehaviorSettingKeys()
    {
        return
        [
            "silent-start",
            "minimize-to-tray",
            "lightweight-mode",
            "tray-double-click",
            "lazy-mode",
            "titlebar-fps",
            "auto-start",
            "window-toggle-hotkey",
            "system-proxy-toggle-hotkey",
            "tun-toggle-hotkey"
        ];
    }

    private static async Task SetAppBehaviorSettingAsync(
        MainWindowViewModel viewModel,
        string key,
        string value)
    {
        var behavior = viewModel.AppBehavior;
        var normalizedValue = NormalizeInputValue(value);
        switch (key.ToLowerInvariant())
        {
            case "silent-start": behavior.IsSilentStartEnabled = ParseBool(normalizedValue); break;
            case "minimize-to-tray": behavior.IsMinimizeToTrayEnabled = ParseBool(normalizedValue); break;
            case "lightweight-mode": behavior.IsLightweightModeEnabled = ParseBool(normalizedValue); break;
            case "tray-double-click": behavior.IsTrayDoubleClickEnabled = ParseBool(normalizedValue); break;
            case "lazy-mode": behavior.IsLazyModeEnabled = ParseBool(normalizedValue); break;
            case "titlebar-fps": behavior.IsTitleBarFpsVisible = ParseBool(normalizedValue); break;
            case "auto-start": behavior.SetAutoStartEnabled(ParseBool(normalizedValue)); break;
            case "window-toggle-hotkey": await behavior.SetWindowToggleHotkeyAsync(normalizedValue); break;
            case "system-proxy-toggle-hotkey": await behavior.SetSystemProxyToggleHotkeyAsync(normalizedValue); break;
            case "tun-toggle-hotkey": await behavior.SetTunToggleHotkeyAsync(normalizedValue); break;
            default: throw new InvalidOperationException($"Unknown app behavior setting: {key}");
        }
    }

    private static IReadOnlyList<string> SystemIntegrationSettingKeys()
    {
        return
        [
            "proxy-host",
            "proxy-bypass",
            "pac-mode",
            "pac-script"
        ];
    }

    private static void SetSystemIntegrationSetting(MainWindowViewModel viewModel, string key, string value)
    {
        var system = viewModel.SystemIntegration;
        var normalizedValue = NormalizeInputValue(value);
        switch (key.ToLowerInvariant())
        {
            case "proxy-host": system.ProxyHost = normalizedValue; break;
            case "proxy-bypass": system.SystemProxyBypass = NormalizeListInput(normalizedValue); break;
            case "pac-mode": system.IsPacModeEnabled = ParseBool(normalizedValue); break;
            case "pac-script": system.PacScript = normalizedValue; break;
            default: throw new InvalidOperationException($"Unknown system integration setting: {key}");
        }
    }

    private static AppTheme ParseAppTheme(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "system" or "sys" => AppTheme.System,
            "light" => AppTheme.Light,
            "dark" => AppTheme.Dark,
            _ => throw new InvalidOperationException($"Unknown theme: {value}")
        };
    }

    private static AccentColorMode ParseAccentColorMode(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "system" or "sys" => AccentColorMode.System,
            "custom" => AccentColorMode.Custom,
            _ => throw new InvalidOperationException($"Unknown accent mode: {value}")
        };
    }

    private static WindowEffect ParseWindowEffect(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "none" => WindowEffect.None,
            "mica" => WindowEffect.Mica,
            "acrylic" => WindowEffect.Acrylic,
            "blur" => WindowEffect.Blur,
            _ => throw new InvalidOperationException($"Unknown window effect: {value}")
        };
    }
}
#endif
