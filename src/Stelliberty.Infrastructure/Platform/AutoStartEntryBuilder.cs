using System.Text;
using Stelliberty.Application.Platform;

namespace Stelliberty.Infrastructure.Platform;

public static class AutoStartEntryBuilder
{
#if DEBUG
    private const string EntryName = AppMetadata.Name + "-dev";
    private const string LaunchAgentLabel = "com." + AppMetadata.Name + ".app.dev";
    private const string DisplayName = AppMetadata.DisplayName + " Dev";
#else
    private const string EntryName = AppMetadata.Name;
    private const string LaunchAgentLabel = "com." + AppMetadata.Name + ".app";
    private const string DisplayName = AppMetadata.DisplayName;
#endif

    public const string WindowsTaskFilePrefix = EntryName;
    public const string WindowsTaskName = DisplayName;
    public const string LinuxDesktopFileName = EntryName + ".desktop";
    public const string MacOSLaunchAgentFileName = LaunchAgentLabel + ".plist";

    public static string WindowsScheduledTaskXml(
        string binaryPath,
        string? userId = null,
        bool isSilentStartEnabled = false)
    {
        var workingDirectory = Path.GetDirectoryName(binaryPath);
        var lines = new List<string>
        {
            """<?xml version="1.0" encoding="UTF-16"?>""",
            """<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">""",
            "  <RegistrationInfo>",
            $"    <Description>{Xml(DisplayName)} auto start</Description>",
            "  </RegistrationInfo>",
            "  <Triggers>",
            "    <LogonTrigger>",
            "      <Enabled>true</Enabled>",
            "      <Delay>PT1S</Delay>"
        };

        if (!string.IsNullOrWhiteSpace(userId))
        {
            lines.Add($"      <UserId>{Xml(userId)}</UserId>");
        }

        lines.AddRange(
        [
            "    </LogonTrigger>",
            "  </Triggers>",
            "  <Principals>",
            """    <Principal id="Author">"""
        ]);

        if (!string.IsNullOrWhiteSpace(userId))
        {
            lines.Add($"      <UserId>{Xml(userId)}</UserId>");
        }

        lines.Add("      <LogonType>InteractiveToken</LogonType>");
        lines.Add("      <RunLevel>LeastPrivilege</RunLevel>");
        lines.AddRange(
        [
            "    </Principal>",
            "  </Principals>",
            "  <Settings>",
            "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>",
            "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>",
            "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>",
            "    <AllowHardTerminate>true</AllowHardTerminate>",
            "    <StartWhenAvailable>true</StartWhenAvailable>",
            "    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>",
            "    <AllowStartOnDemand>true</AllowStartOnDemand>",
            "    <Enabled>true</Enabled>",
            "    <Hidden>false</Hidden>",
            "    <RunOnlyIfIdle>false</RunOnlyIfIdle>",
            "    <WakeToRun>false</WakeToRun>",
            "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>",
            "    <Priority>4</Priority>",
            "  </Settings>",
            """  <Actions Context="Author">""",
            "    <Exec>",
            $"      <Command>{Xml(binaryPath)}</Command>"
        ]);

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            lines.Add($"      <WorkingDirectory>{Xml(workingDirectory)}</WorkingDirectory>");
        }

        if (isSilentStartEnabled)
        {
            lines.Add("      <Arguments>--silent-start</Arguments>");
        }

        lines.AddRange(
        [
            "    </Exec>",
            "  </Actions>",
            "</Task>",
            string.Empty
        ]);

        return string.Join('\n', lines);
    }

    public static string LinuxDesktopEntry(string binaryPath, bool isSilentStartEnabled)
    {
        var exec = string.Join(' ', [DesktopExecArgument(binaryPath), .. OptionalSilentArgument(isSilentStartEnabled)]);
        return string.Join('\n',
            "[Desktop Entry]",
            "Type=Application",
            $"Name={DisplayName}",
            $"Exec={exec}",
            "Terminal=false",
            "X-GNOME-Autostart-enabled=true",
            string.Empty);
    }

    public static string MacOSLaunchAgentPlist(string binaryPath, bool isSilentStartEnabled)
    {
        var lines = new List<string>
        {
            """<?xml version="1.0" encoding="UTF-8"?>""",
            """<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">""",
            """<plist version="1.0">""",
            "<dict>",
            "    <key>Label</key>",
            $"    <string>{Xml(LaunchAgentLabel)}</string>",
            "    <key>ProgramArguments</key>",
            "    <array>",
            $"        <string>{Xml(binaryPath)}</string>"
        };

        if (isSilentStartEnabled)
        {
            lines.Add("        <string>--silent-start</string>");
        }

        lines.AddRange(
        [
            "    </array>",
            "    <key>RunAtLoad</key>",
            "    <true/>",
            "</dict>",
            "</plist>",
            string.Empty
        ]);

        return string.Join('\n', lines);
    }

    private static IReadOnlyList<string> OptionalSilentArgument(bool isSilentStartEnabled)
    {
        return isSilentStartEnabled ? ["--silent-start"] : [];
    }

    private static string DesktopExecArgument(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var ch in value)
        {
            if (ch is '\\' or '"' or '$' or '`')
            {
                builder.Append('\\');
            }
            builder.Append(ch);
        }
        builder.Append('"');
        return builder.ToString();
    }

    private static string Xml(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);
    }
}
