using System.Runtime.InteropServices;

namespace ThoriumInbox.Core;

public static class ReleaseAssets
{
    public const string Manifest = "update-manifest.json";
    public const string ManifestSignature = "update-manifest.sig";
    public const string WindowsX64 = "ThoriumInbox-Setup-x64.exe";
    public const string LinuxX64 = "ThoriumInbox-linux-x64.deb";
    public const string MacX64 = "ThoriumInbox-macos-x64.pkg";
    public const string MacArm64 = "ThoriumInbox-macos-arm64.pkg";

    public static string? CurrentInstaller()
    {
        var architecture = RuntimeInformation.ProcessArchitecture;
        if (OperatingSystem.IsWindows() && architecture == Architecture.X64)
            return WindowsX64;
        if (OperatingSystem.IsLinux() && architecture == Architecture.X64)
            return LinuxX64;
        if (OperatingSystem.IsMacOS())
            return architecture == Architecture.Arm64 ? MacArm64 : architecture == Architecture.X64 ? MacX64 : null;
        return null;
    }

    public static bool IsInstaller(string name) => name is WindowsX64 or LinuxX64 or MacX64 or MacArm64;
}
