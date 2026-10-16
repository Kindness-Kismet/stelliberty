using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;

namespace Stelliberty.Infrastructure.Platform;

public sealed class WindowsUwpLoopbackService : IUwpLoopbackService
{
    // NetworkIsolation 同时返回 Win32 代码和 HRESULT。
    private const uint ForceComputeBinariesFlag = 1;
    private const uint AccessDenied = 5;
    private const uint HResultAccessDenied = 0x80070005;
    private const uint BadImpersonationLevel = 1346;
    private const uint HResultBadImpersonationLevel = 0x80070542;
    private const int DebugLogLimit = 10;

    public IReadOnlyList<UwpLoopbackPackage> LoadPackages()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        try
        {
            var snapshot = LoadSnapshot();
            AppLogger.Info($"UWP loopback app enumeration completed: count={snapshot.Packages.Count}");
            return snapshot.Packages.Select(package => package.ToPackage()).ToArray();
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"UWP loopback app enumeration failed: {exception}");
            return [];
        }
    }

    public UwpLoopbackOperationResult SetLoopback(string packageFamilyName, bool isEnabled)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new UwpLoopbackOperationResult(false, "UWP loopback configuration is not supported in this environment", null);
        }

        if (string.IsNullOrWhiteSpace(packageFamilyName))
        {
            return new UwpLoopbackOperationResult(false, "Package family name is empty", null);
        }

        try
        {
            var snapshot = LoadSnapshot();
            var target = snapshot.Packages.FirstOrDefault(package => string.Equals(package.PackageFamilyName, packageFamilyName, StringComparison.Ordinal));
            if (target is null)
            {
                return new UwpLoopbackOperationResult(false, $"Package not found: {packageFamilyName}", null);
            }

            var nextSids = UwpLoopbackConfiguration.BuildNextLoopbackSids(snapshot.LoopbackSids, target.Sid, isEnabled);
            var result = SetAppContainerConfig(nextSids);
            if (result == 0)
            {
                var package = target.ToPackage() with { IsLoopbackEnabled = isEnabled };
                var message = isEnabled ? $"Enabled: {package.DisplayName}" : $"Disabled: {package.DisplayName}";
                AppLogger.Info($"UWP loopback configuration updated: {package.PackageFamilyName} enabled={isEnabled}");
                return new UwpLoopbackOperationResult(true, message, package);
            }

            var errorMessage = BuildNetworkIsolationErrorMessage("NetworkIsolationSetAppContainerConfig", result);
            AppLogger.Warning($"UWP loopback configuration failed: {packageFamilyName} enabled={isEnabled}, {errorMessage}");
            return new UwpLoopbackOperationResult(false, errorMessage, target.ToPackage());
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"UWP loopback configuration failed: {packageFamilyName} {exception}");
            return new UwpLoopbackOperationResult(false, exception.Message, null);
        }
    }

    public UwpLoopbackBatchResult SetLoopbackBatch(IReadOnlyCollection<string> enabledPackageFamilyNames)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new UwpLoopbackBatchResult(false, "UWP loopback configuration is not supported in this environment", []);
        }

        try
        {
            var snapshot = LoadSnapshot();
            var enabledSet = new HashSet<string>(enabledPackageFamilyNames, StringComparer.Ordinal);
            var knownSids = new HashSet<byte[]>(snapshot.Packages.Select(package => package.Sid), ByteArrayComparer.Instance);
            // 枚举之外的回环 SID 要保留，避免误删。
            var preservedSids = snapshot.LoopbackSids.Where(sid => !knownSids.Contains(sid));
            var enabledSids = snapshot.Packages
                .Where(package => enabledSet.Contains(package.PackageFamilyName))
                .Select(package => package.Sid);
            var nextSids = preservedSids.Concat(enabledSids).ToArray();

            var result = SetAppContainerConfig(nextSids);
            if (result == 0)
            {
                var packages = snapshot.Packages
                    .Select(package => package.ToPackage() with { IsLoopbackEnabled = enabledSet.Contains(package.PackageFamilyName) })
                    .ToArray();
                var enabledCount = packages.Count(package => package.IsLoopbackEnabled);
                AppLogger.Info($"UWP loopback batch commit completed: enabled={enabledCount}");
                return new UwpLoopbackBatchResult(true, $"Saved, enabled {enabledCount}", packages);
            }

            var errorMessage = BuildNetworkIsolationErrorMessage("NetworkIsolationSetAppContainerConfig", result);
            AppLogger.Warning($"UWP loopback batch commit failed: {errorMessage}");
            return new UwpLoopbackBatchResult(false, errorMessage, []);
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"UWP loopback batch commit failed: {exception}");
            return new UwpLoopbackBatchResult(false, exception.Message, []);
        }
    }

    private static string SidToString(byte[] sid)
    {
        // SecurityIdentifier 仅限 Windows；平台守卫满足 CA1416。
        if (!OperatingSystem.IsWindows())
        {
            return string.Empty;
        }

        try
        {
            return new SecurityIdentifier(sid, 0).Value;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    private static UwpLoopbackSnapshot LoadSnapshot()
    {
        AppLogger.Info("Starting UWP app container enumeration");
        var containers = IntPtr.Zero;
        try
        {
            var count = TryEnumAppContainers(out containers);
            // 没有可枚举应用时，也不能丢弃现有豁免。
            var loopbackSids = ReadCurrentLoopbackSids();
            if (count == 0 || containers == IntPtr.Zero)
            {
                AppLogger.Warning("No UWP app container was found");
                return new UwpLoopbackSnapshot([], loopbackSids);
            }

            var loopbackSidSet = new HashSet<byte[]>(loopbackSids, ByteArrayComparer.Instance);
            var packages = ReadAppContainers(containers, count, loopbackSidSet);
            return new UwpLoopbackSnapshot(packages, loopbackSids);
        }
        finally
        {
            if (containers != IntPtr.Zero)
            {
                NetworkIsolationFreeAppContainers(containers);
            }
        }
    }

    private static uint TryEnumAppContainers(out IntPtr containers)
    {
        foreach (var flag in new[] { ForceComputeBinariesFlag, 0u })
        {
            var result = NetworkIsolationEnumAppContainers(flag, out var count, out containers);
            if (result == 0)
            {
                AppLogger.Info($"NetworkIsolationEnumAppContainers succeeded: flags={flag} count={count}");
                return count;
            }

            // 二进制信息计算受权限或模拟级别限制时，仍可尝试基础枚举。
            var canUseBasicEnumeration = result is AccessDenied or HResultAccessDenied or BadImpersonationLevel or HResultBadImpersonationLevel;
            if (flag != 0 && canUseBasicEnumeration)
            {
                AppLogger.Warning($"Full UWP app container enumeration failed; falling back to basic enumeration: {BuildNetworkIsolationErrorMessage("NetworkIsolationEnumAppContainers", result)}");
                continue;
            }

            containers = IntPtr.Zero;
            throw new InvalidOperationException(BuildNetworkIsolationErrorMessage("NetworkIsolationEnumAppContainers", result));
        }

        containers = IntPtr.Zero;
        return 0;
    }

    private static IReadOnlyList<byte[]> ReadCurrentLoopbackSids()
    {
        var result = NetworkIsolationGetAppContainerConfig(out var count, out var sids);
        if (result != 0)
        {
            // 读取失败不能代表空配置，否则保存会覆盖现有豁免。
            throw new InvalidOperationException(BuildNetworkIsolationErrorMessage("NetworkIsolationGetAppContainerConfig", result));
        }

        try
        {
            if (count == 0)
            {
                return [];
            }

            if (sids == IntPtr.Zero)
            {
                throw new InvalidOperationException("NetworkIsolationGetAppContainerConfig returned no SID buffer for a non-empty configuration");
            }

            var sidSize = Marshal.SizeOf<SidAndAttributes>();
            var resultSids = new List<byte[]>((int)count);
            for (var index = 0; index < count; index++)
            {
                var item = Marshal.PtrToStructure<SidAndAttributes>(IntPtr.Add(sids, index * sidSize));
                var sidBytes = CopySidBytes(item.Sid)
                    ?? throw new InvalidOperationException("UWP loopback configuration contains an invalid SID");
                resultSids.Add(sidBytes);
            }

            return resultSids;
        }
        finally
        {
            if (sids != IntPtr.Zero)
            {
                LocalFree(sids);
            }
        }
    }

    private static IReadOnlyList<WindowsUwpLoopbackPackage> ReadAppContainers(IntPtr containers, uint count, HashSet<byte[]> loopbackSidSet)
    {
        var containerSize = Marshal.SizeOf<InetFirewallAppContainer>();
        var packages = new List<WindowsUwpLoopbackPackage>((int)count);
        for (var index = 0; index < count; index++)
        {
            var container = Marshal.PtrToStructure<InetFirewallAppContainer>(IntPtr.Add(containers, index * containerSize));
            var sidBytes = CopySidBytes(container.AppContainerSid);
            if (sidBytes is null)
            {
                continue;
            }

            var packageFamilyName = PtrToString(container.PackageFullName);
            if (string.IsNullOrWhiteSpace(packageFamilyName))
            {
                continue;
            }

            var displayName = PtrToString(container.DisplayName);
            var appContainerName = PtrToString(container.AppContainerName);
            var package = new WindowsUwpLoopbackPackage(
                packageFamilyName,
                UwpLoopbackConfiguration.ResolveDisplayName(displayName, appContainerName, packageFamilyName),
                appContainerName,
                sidBytes,
                loopbackSidSet.Contains(sidBytes));
            packages.Add(package);

            if (index < DebugLogLimit)
            {
                AppLogger.Debug($"UWP container: {package.DisplayName} {package.PackageFamilyName} enabled={package.IsLoopbackEnabled}");
            }
        }

        // 按包名去重，因为重复 SID 会破坏批量写入。
        return packages
            .DistinctBy(package => package.PackageFamilyName, StringComparer.Ordinal)
            .OrderBy(package => package.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static uint SetAppContainerConfig(IReadOnlyList<byte[]> sids)
    {
        if (sids.Count == 0)
        {
            return NetworkIsolationSetAppContainerConfig(0, IntPtr.Zero);
        }

        var sidPointers = new List<IntPtr>(sids.Count);
        var arrayPointer = IntPtr.Zero;
        try
        {
            var sidAndAttributesSize = Marshal.SizeOf<SidAndAttributes>();
            arrayPointer = Marshal.AllocHGlobal(sidAndAttributesSize * sids.Count);
            for (var index = 0; index < sids.Count; index++)
            {
                var sidPointer = Marshal.AllocHGlobal(sids[index].Length);
                // 验证前先注册，保证 finally 总能释放缓冲区。
                sidPointers.Add(sidPointer);
                Marshal.Copy(sids[index], 0, sidPointer, sids[index].Length);
                if (!IsValidSid(sidPointer))
                {
                    throw new InvalidOperationException("SID byte structure is invalid");
                }

                Marshal.StructureToPtr(new SidAndAttributes(sidPointer, 0), IntPtr.Add(arrayPointer, index * sidAndAttributesSize), false);
            }

            return NetworkIsolationSetAppContainerConfig((uint)sids.Count, arrayPointer);
        }
        finally
        {
            foreach (var sidPointer in sidPointers)
            {
                Marshal.FreeHGlobal(sidPointer);
            }

            if (arrayPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(arrayPointer);
            }
        }
    }

    private static byte[]? CopySidBytes(IntPtr sid)
    {
        if (sid == IntPtr.Zero || !IsValidSid(sid))
        {
            return null;
        }

        var length = GetLengthSid(sid);
        if (length < 8)
        {
            return null;
        }

        var bytes = new byte[length];
        Marshal.Copy(sid, bytes, 0, (int)length);
        return bytes;
    }

    private static string PtrToString(IntPtr value)
    {
        return Marshal.PtrToStringUni(value) ?? string.Empty;
    }

    private static string BuildNetworkIsolationErrorMessage(string apiName, uint errorCode)
    {
        var win32Code = ExtractWin32ErrorCode(errorCode);
        var systemMessage = win32Code is null ? "Unknown error" : new Win32Exception((int)win32Code.Value).Message;
        return $"{apiName} failed: 0x{errorCode:X8}, {ExplainNetworkIsolationError(errorCode)}, {systemMessage}";
    }

    private static uint? ExtractWin32ErrorCode(uint errorCode)
    {
        // HRESULT_FROM_WIN32 使用 0x80070000，并把 Win32 代码放在低 16 位。
        if ((errorCode & 0xFFFF0000) == 0x80070000)
        {
            return errorCode & 0xFFFF;
        }

        return errorCode <= ushort.MaxValue ? errorCode : null;
    }

    private static string ExplainNetworkIsolationError(uint errorCode)
    {
        return errorCode switch
        {
            AccessDenied or HResultAccessDenied => "Insufficient permissions, usually requiring administrator rights or a system-protected target",
            BadImpersonationLevel or HResultBadImpersonationLevel => "Required impersonation level was not provided or is invalid",
            0x80070057 or 87 => "Invalid argument, usually caused by SID configuration that does not meet system requirements",
            0x80004005 => "System restriction or low-level component refused the operation",
            0x00000490 => "Target container or configuration entry was not found",
            _ => "Unknown error"
        };
    }

    [DllImport("firewallapi.dll")]
    private static extern uint NetworkIsolationEnumAppContainers(uint flags, out uint count, out IntPtr containers);

    [DllImport("firewallapi.dll")]
    private static extern void NetworkIsolationFreeAppContainers(IntPtr containers);

    [DllImport("firewallapi.dll")]
    private static extern uint NetworkIsolationGetAppContainerConfig(out uint count, out IntPtr sids);

    [DllImport("firewallapi.dll")]
    private static extern uint NetworkIsolationSetAppContainerConfig(uint count, IntPtr sids);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    [DllImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsValidSid(IntPtr sid);

    [DllImport("advapi32.dll")]
    private static extern uint GetLengthSid(IntPtr sid);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct UwpLoopbackSnapshot(IReadOnlyList<WindowsUwpLoopbackPackage> Packages, IReadOnlyList<byte[]> LoopbackSids);

    private sealed record WindowsUwpLoopbackPackage(string PackageFamilyName, string DisplayName, string AppContainerName, byte[] Sid, bool IsLoopbackEnabled)
    {
        public UwpLoopbackPackage ToPackage()
        {
            return new UwpLoopbackPackage(PackageFamilyName, DisplayName, IsLoopbackEnabled, AppContainerName, SidToString(Sid));
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes
    {
        public IntPtr Sid;
        public uint Attributes;

        public SidAndAttributes(IntPtr sid, uint attributes)
        {
            Sid = sid;
            Attributes = attributes;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InetFirewallAcCapabilities
    {
        public uint Count;
        public IntPtr Capabilities;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InetFirewallAcBinaries
    {
        public uint Count;
        public IntPtr Binaries;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InetFirewallAppContainer
    {
        public IntPtr AppContainerSid;
        public IntPtr UserSid;
        public IntPtr AppContainerName;
        public IntPtr DisplayName;
        public IntPtr Description;
        public InetFirewallAcCapabilities Capabilities;
        public InetFirewallAcBinaries Binaries;
        public IntPtr WorkingDirectory;
        public IntPtr PackageFullName;
    }

    private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        public static ByteArrayComparer Instance { get; } = new();

        public bool Equals(byte[]? x, byte[]? y)
        {
            return ReferenceEquals(x, y) || x is not null && y is not null && x.SequenceEqual(y);
        }

        public int GetHashCode(byte[] value)
        {
            var hash = new HashCode();
            foreach (var item in value)
            {
                hash.Add(item);
            }

            return hash.ToHashCode();
        }
    }
}
