using System.Xml.Linq;

namespace ThoriumInbox.Core;

public static class LoginStartupFiles
{
    private const string MacLabel = "io.github.katus4.thoriuminbox";

    public static bool IsLinuxEnabled(string configDirectory)
    {
        string path = LinuxPath(configDirectory);
        return File.Exists(path)
            && !File.ReadLines(path).Any(line => line.Trim().Equals("Hidden=true", StringComparison.OrdinalIgnoreCase));
    }

    public static void SetLinuxEnabled(string configDirectory, bool enabled) =>
        SetFile(LinuxPath(configDirectory), enabled ? """
            [Desktop Entry]
            Type=Application
            Name=Thorium Inbox
            Exec=/usr/bin/thorium-inbox --background
            Icon=thorium-inbox
            Terminal=false
            """ + "\n" : null);

    public static bool IsMacEnabled(string userHome) => File.Exists(MacPath(userHome));

    public static void SetMacEnabled(string userHome, string bundlePath, bool enabled)
    {
        if (!enabled)
        {
            SetFile(MacPath(userHome), null);
            return;
        }

        var plist = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XDocumentType("plist", "-//Apple//DTD PLIST 1.0//EN", "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null),
            new XElement("plist", new XAttribute("version", "1.0"),
                new XElement("dict",
                    new XElement("key", "Label"), new XElement("string", MacLabel),
                    new XElement("key", "ProgramArguments"),
                    new XElement("array", new[] { "/usr/bin/open", "-a", bundlePath, "--args", "--background" }
                        .Select(argument => new XElement("string", argument))),
                    new XElement("key", "RunAtLoad"), new XElement("true"))));
        SetFile(MacPath(userHome), plist + "\n");
    }

    private static string LinuxPath(string configDirectory) => Path.Combine(configDirectory, "autostart", "thorium-inbox.desktop");

    private static string MacPath(string userHome) => Path.Combine(userHome, "Library", "LaunchAgents", MacLabel + ".plist");

    private static void SetFile(string path, string? contents)
    {
        if (contents is null)
        {
            if (File.Exists(path))
                File.Delete(path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, contents);
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }
}
