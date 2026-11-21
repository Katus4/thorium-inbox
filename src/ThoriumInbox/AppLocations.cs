using Microsoft.Win32;

namespace ThoriumInbox;

internal static class AppLocations
{
    public const string SettingsKey = @"Software\Thorium Inbox";
    public const string InboxValue = "InboxFolder";

    private static readonly string DocumentsRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Thorium Inbox");

    public static string Epubs { get; } = Path.Combine(DocumentsRoot, "EPUBs");
    public static string AppData { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Thorium Inbox");
    public static string History { get; } = Path.Combine(AppData, "imports.json");
    public static string Activity { get; } = Path.Combine(AppData, "activity.log");

    public static string ReadInbox() =>
        Registry.CurrentUser.OpenSubKey(SettingsKey) is { } key ? ReadAndClose(key) : DefaultInbox;

    public static string ValidateInbox(string path)
    {
        string inbox = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (string.Equals(inbox, Epubs, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a folder separate from the EPUB output folder.");
        Directory.CreateDirectory(inbox);
        return inbox;
    }

    public static void WriteInbox(string path)
    {
        using var key = Registry.CurrentUser.CreateSubKey(SettingsKey);
        key.SetValue(InboxValue, path, RegistryValueKind.String);
    }

    private static string DefaultInbox => Path.Combine(DocumentsRoot, "Inbox");

    private static string ReadAndClose(RegistryKey key)
    {
        using (key)
            return key.GetValue(InboxValue) as string is { Length: > 0 } current ? current : DefaultInbox;
    }
}
