using System.Runtime.Versioning;
using Microsoft.Win32;

namespace ThoriumInbox;

internal static class AppLocations
{
    public const string SettingsKey = @"Software\Thorium Inbox";
    public const string InboxValue = "InboxFolder";

    public static string UserHome { get; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly string DocumentsRoot = Path.Combine(DocumentsFolder(), "Thorium Inbox");
    public static string LinuxConfig { get; } = XdgFolder("XDG_CONFIG_HOME", ".config");

    public static string Epubs { get; } = Path.Combine(DocumentsRoot, "EPUBs");
    public static string AppData { get; } = Path.Combine(DataFolder(), "Thorium Inbox");
    public static string History { get; } = Path.Combine(AppData, "imports.json");
    public static string Activity { get; } = Path.Combine(AppData, "activity.log");
    public static string OpenWindowSignal { get; } = Path.Combine(AppData, "show-window");

    public static string ReadInbox() =>
        OperatingSystem.IsWindows() ? ReadWindowsInbox() : ReadPreference("inbox.txt") ?? DefaultInbox;

    public static string ValidateInbox(string path)
    {
        string inbox = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(inbox, Epubs, comparison))
            throw new ArgumentException("Choose a folder separate from the EPUB output folder.");
        Directory.CreateDirectory(inbox);
        return inbox;
    }

    public static void WriteInbox(string path)
    {
        if (OperatingSystem.IsWindows())
            WriteWindowsInbox(path);
        else
            WritePreference("inbox.txt", path);
    }

    public static string? ReadPreference(string name)
    {
        string path = Path.Combine(AppData, name);
        return File.Exists(path) && File.ReadAllText(path) is { Length: > 0 } value ? value : null;
    }

    public static void WritePreference(string name, string value)
    {
        Directory.CreateDirectory(AppData);
        string path = Path.Combine(AppData, name);
        string temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, value);
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    private static string DocumentsFolder()
    {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return documents.Length > 0 ? documents : Path.Combine(UserHome, "Documents");
    }

    private static string DataFolder()
    {
        if (OperatingSystem.IsMacOS())
            return Path.Combine(UserHome, "Library", "Application Support");
        if (OperatingSystem.IsLinux())
            return XdgFolder("XDG_DATA_HOME", Path.Combine(".local", "share"));
        return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }

    private static string XdgFolder(string variable, string fallback)
    {
        string? value = Environment.GetEnvironmentVariable(variable);
        return value is not null && Path.IsPathFullyQualified(value) ? value : Path.Combine(UserHome, fallback);
    }

    [SupportedOSPlatform("windows")]
    private static void WriteWindowsInbox(string path)
    {
        using var key = Registry.CurrentUser.CreateSubKey(SettingsKey);
        key.SetValue(InboxValue, path, RegistryValueKind.String);
    }

    private static string DefaultInbox => Path.Combine(DocumentsRoot, "Inbox");

    [SupportedOSPlatform("windows")]
    private static string ReadWindowsInbox()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKey);
        return key?.GetValue(InboxValue) as string is { Length: > 0 } current ? current : DefaultInbox;
    }
}
