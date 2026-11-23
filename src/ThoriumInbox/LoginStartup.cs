using System.Runtime.Versioning;
using Microsoft.Win32;
using ThoriumInbox.Core;

namespace ThoriumInbox;

internal static class LoginStartup
{
    private const string Preference = "login-startup.txt";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "Thorium Inbox";
    private const string LinuxExecutable = "/opt/thorium-inbox/ThoriumInbox";
    private static string? MacBundle => InstalledMacBundle(Environment.ProcessPath);

    public static bool IsAvailable => OperatingSystem.IsWindows()
        || (OperatingSystem.IsLinux() && Environment.ProcessPath == LinuxExecutable)
        || (OperatingSystem.IsMacOS() && MacBundle is not null);

    public static bool Initialize()
    {
        if (!IsAvailable)
            return false;

        string? preference = AppLocations.ReadPreference(Preference);
        if (preference is null && !OperatingSystem.IsWindows())
            SetEnabled(true);
        else if (preference == "disabled" && OperatingSystem.IsWindows() && IsWindowsEnabled())
            SetWindowsEnabled(false);
        return IsEnabled();
    }

    public static void SetEnabled(bool enabled)
    {
        if (OperatingSystem.IsWindows())
            SetWindowsEnabled(enabled);
        else if (OperatingSystem.IsLinux() && IsAvailable)
            LoginStartupFiles.SetLinuxEnabled(AppLocations.LinuxConfig, enabled);
        else if (OperatingSystem.IsMacOS() && MacBundle is { } bundle)
            LoginStartupFiles.SetMacEnabled(AppLocations.UserHome, bundle, enabled);
        else
            throw new InvalidOperationException("Install Thorium Inbox before setting up login startup.");

        AppLocations.WritePreference(Preference, enabled ? "enabled" : "disabled");
    }

    private static bool IsEnabled()
    {
        if (OperatingSystem.IsWindows())
            return IsWindowsEnabled();
        if (OperatingSystem.IsLinux())
            return LoginStartupFiles.IsLinuxEnabled(AppLocations.LinuxConfig);
        return OperatingSystem.IsMacOS() && LoginStartupFiles.IsMacEnabled(AppLocations.UserHome);
    }

    private static string? InstalledMacBundle(string? executable)
    {
        if (executable is null)
            return null;
        var macos = Directory.GetParent(executable);
        if (macos?.Name != "MacOS" || macos.Parent?.Name != "Contents" || macos.Parent.Parent is not { } bundle
            || bundle.Name != "Thorium Inbox.app")
            return null;

        string? parent = bundle.Parent?.FullName;
        return parent == "/Applications" || parent == Path.Combine(AppLocations.UserHome, "Applications") ? bundle.FullName : null;
    }

    [SupportedOSPlatform("windows")]
    private static bool IsWindowsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunName) is string;
    }

    [SupportedOSPlatform("windows")]
    private static void SetWindowsEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(RunName, $"\"{Environment.ProcessPath}\" --background", RegistryValueKind.String);
        else
            key.DeleteValue(RunName, throwOnMissingValue: false);
    }
}
