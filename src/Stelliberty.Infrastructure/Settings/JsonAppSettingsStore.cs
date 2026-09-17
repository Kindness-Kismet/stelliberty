using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Settings;
using Stelliberty.Infrastructure.Storage;

namespace Stelliberty.Infrastructure.Settings;

public sealed class JsonAppSettingsStore : IAppSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private static readonly System.Reflection.PropertyInfo[] SettingsProperties = typeof(AppSettings)
        .GetProperties()
        .Where(property => property.CanRead && property.CanWrite)
        .ToArray();

    private readonly string _settingsPath;
    private readonly string _mutexName;
    private readonly object _gate = new();
    private readonly ConditionalWeakTable<AppSettings, JsonObject> _snapshots = new();

    public JsonAppSettingsStore(IPlatformDirectories platformDirectories)
    {
        Directory.CreateDirectory(platformDirectories.AppDataDirectory);
        _settingsPath = platformDirectories.SettingsFilePath;
        _mutexName = CreateMutexName(_settingsPath);
    }

    public AppSettings Load()
    {
        lock (_gate)
        {
            var settings = WithFileLock(() =>
            {
                if (File.Exists(_settingsPath))
                {
                    return ReadFromDisk();
                }

                var defaults = new AppSettings();
                WriteToDisk(defaults);
                return defaults;
            });
            TrackSnapshot(settings);
            return settings;
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_gate)
        {
            _snapshots.TryGetValue(settings, out var baseline);
            var merged = WithFileLock(() =>
            {
                var current = File.Exists(_settingsPath) ? ReadFromDisk() : new AppSettings();
                var result = MergeChanges(settings, baseline, current);
                WriteToDisk(result);
                return result;
            });
            CopySettings(merged, settings);
            TrackSnapshot(settings);
        }
    }

    private AppSettings ReadFromDisk()
    {
        // 损坏文件先备份成 .corrupt 再回默认，原配置可救回。
        return Normalize(JsonFileRecovery.ReadOrRecover<AppSettings>(_settingsPath) ?? new AppSettings());
    }

    private void WriteToDisk(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        AtomicFile.WriteAllText(_settingsPath, json);
    }

    private static AppSettings MergeChanges(AppSettings settings, JsonObject? baseline, AppSettings current)
    {
        if (baseline is null)
        {
            return Normalize(Clone(settings));
        }

        var candidate = ToJsonObject(settings);
        var merged = ToJsonObject(current);
        foreach (var property in candidate)
        {
            baseline.TryGetPropertyValue(property.Key, out var previousValue);
            if (!JsonNode.DeepEquals(property.Value, previousValue))
            {
                merged[property.Key] = property.Value?.DeepClone();
            }
        }

        return Normalize(merged.Deserialize<AppSettings>() ?? new AppSettings());
    }

    private static AppSettings Clone(AppSettings settings) =>
        ToJsonObject(settings).Deserialize<AppSettings>() ?? new AppSettings();

    private static JsonObject ToJsonObject(AppSettings settings) =>
        JsonSerializer.SerializeToNode(settings)?.AsObject()
        ?? throw new InvalidOperationException("App settings could not be serialized.");

    private static void CopySettings(AppSettings source, AppSettings target)
    {
        foreach (var property in SettingsProperties)
        {
            property.SetValue(target, property.GetValue(source));
        }
    }

    private void TrackSnapshot(AppSettings settings)
    {
        _snapshots.Remove(settings);
        _snapshots.Add(settings, ToJsonObject(settings));
    }

    private T WithFileLock<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, _mutexName);
        var ownsMutex = false;
        try
        {
            try
            {
                mutex.WaitOne();
            }
            catch (AbandonedMutexException)
            {
                // 前一进程异常退出后当前线程已获得互斥锁。
            }
            ownsMutex = true;
            return action();
        }
        finally
        {
            if (ownsMutex)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    private static string CreateMutexName(string settingsPath)
    {
        var normalizedPath = Path.GetFullPath(settingsPath);
        if (OperatingSystem.IsWindows())
        {
            normalizedPath = normalizedPath.ToUpperInvariant();
        }

        var pathHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath)));
        return OperatingSystem.IsWindows()
            ? $@"Local\Stelliberty.AppSettings.{pathHash}"
            : $"Stelliberty.AppSettings.{pathHash}";
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        var defaults = new AppSettings();
        settings.Language ??= defaults.Language;
        settings.Theme ??= defaults.Theme;
        settings.AccentColorMode ??= defaults.AccentColorMode;
        settings.AccentColor ??= defaults.AccentColor;
        settings.WindowEffect ??= defaults.WindowEffect;
        settings.WindowToggleHotkey ??= defaults.WindowToggleHotkey;
        settings.SystemProxyToggleHotkey ??= defaults.SystemProxyToggleHotkey;
        settings.TunToggleHotkey ??= defaults.TunToggleHotkey;
        settings.AppUpdateCheckInterval ??= defaults.AppUpdateCheckInterval;
        settings.AppUpdateChannel ??= defaults.AppUpdateChannel;
        if (!string.Equals(settings.AppUpdateChannel, "stable", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(settings.AppUpdateChannel, "beta", StringComparison.OrdinalIgnoreCase))
        {
            settings.AppUpdateChannel = defaults.AppUpdateChannel;
        }
        else
        {
            settings.AppUpdateChannel = settings.AppUpdateChannel.Trim().ToLowerInvariant();
        }
        settings.IgnoredUpdateVersion ??= defaults.IgnoredUpdateVersion;
        settings.WebDavUrl ??= defaults.WebDavUrl;
        settings.WebDavUserName ??= defaults.WebDavUserName;
        settings.WebDavPassword ??= defaults.WebDavPassword;
        settings.WebDavRemoteDirectory ??= defaults.WebDavRemoteDirectory;
        if (settings.WebDavBackupIntervalHours <= 0)
        {
            settings.WebDavBackupIntervalHours = defaults.WebDavBackupIntervalHours;
        }

        if (settings.WebDavBackupRetentionCount <= 0)
        {
            settings.WebDavBackupRetentionCount = defaults.WebDavBackupRetentionCount;
        }

        settings.LastCoreVersion ??= defaults.LastCoreVersion;
        settings.DelayTestUrl ??= defaults.DelayTestUrl;
        settings.LanAuthenticationUserName ??= defaults.LanAuthenticationUserName;
        settings.LanAuthenticationPassword ??= defaults.LanAuthenticationPassword;
        settings.LanAllowedIps ??= defaults.LanAllowedIps;
        settings.LanDisallowedIps ??= defaults.LanDisallowedIps;
        settings.SkipAuthPrefixes ??= defaults.SkipAuthPrefixes;
        settings.ExternalControllerAddress ??= defaults.ExternalControllerAddress;
#if DEBUG
        if (settings.ExternalControllerAddress == "127.0.0.1:9090")
        {
            settings.ExternalControllerAddress = defaults.ExternalControllerAddress;
        }
#endif
        settings.ExternalControllerSecret ??= defaults.ExternalControllerSecret;
        settings.ProxyHost ??= defaults.ProxyHost;
        settings.SystemProxyBypass ??= defaults.SystemProxyBypass;
        settings.PacScript ??= defaults.PacScript;
        settings.TunStack ??= defaults.TunStack;
        settings.TunDevice ??= defaults.TunDevice;
        settings.TunDnsHijack ??= defaults.TunDnsHijack;
        settings.TunRouteExcludeAddresses ??= defaults.TunRouteExcludeAddresses;
        settings.DnsListen ??= defaults.DnsListen;
        settings.DnsEnhancedMode ??= defaults.DnsEnhancedMode;
        settings.FakeIpRange ??= defaults.FakeIpRange;
        settings.NameServers ??= defaults.NameServers;
        settings.FallbackNameServers ??= defaults.FallbackNameServers;
        settings.ProxyServerNameServers ??= defaults.ProxyServerNameServers;
        settings.DefaultNameServers ??= defaults.DefaultNameServers;
        settings.FakeIpFilters ??= defaults.FakeIpFilters;
        settings.FallbackFilterGeoIpCode ??= defaults.FallbackFilterGeoIpCode;
        settings.Hosts ??= defaults.Hosts;
        settings.DirectNameServers ??= defaults.DirectNameServers;
        settings.NameServerPolicy ??= defaults.NameServerPolicy;
        settings.FakeIpFilterMode ??= defaults.FakeIpFilterMode;
        settings.FallbackFilterIpCidrs ??= defaults.FallbackFilterIpCidrs;
        settings.FallbackFilterDomains ??= defaults.FallbackFilterDomains;
        settings.GeoDataLoader ??= defaults.GeoDataLoader;
        settings.FindProcessMode ??= defaults.FindProcessMode;
        settings.CoreLogLevel ??= defaults.CoreLogLevel;
        return settings;
    }
}
