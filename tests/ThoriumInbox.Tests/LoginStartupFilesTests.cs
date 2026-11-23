using System.Xml.Linq;
using ThoriumInbox.Core;

namespace ThoriumInbox.Tests;

public sealed class LoginStartupFilesTests
{
    [Fact]
    public void LinuxAutostartUsesInstalledLauncherAndCanBeDisabled()
    {
        var home = Directory.CreateTempSubdirectory("thorium-startup-");
        try
        {
            string file = Path.Combine(home.FullName, "autostart", "thorium-inbox.desktop");
            LoginStartupFiles.SetLinuxEnabled(home.FullName, enabled: true);
            string entry = File.ReadAllText(file);
            Assert.Contains("[Desktop Entry]", entry);
            Assert.Contains("Exec=/usr/bin/thorium-inbox --background", entry);
            Assert.True(LoginStartupFiles.IsLinuxEnabled(home.FullName));

            LoginStartupFiles.SetLinuxEnabled(home.FullName, enabled: false);
            Assert.False(File.Exists(file));
            Assert.False(LoginStartupFiles.IsLinuxEnabled(home.FullName));
        }
        finally { home.Delete(recursive: true); }
    }

    [Fact]
    public void MacLaunchAgentPreservesBundlePathAsOneArgument()
    {
        var home = Directory.CreateTempSubdirectory("thorium-startup-");
        try
        {
            const string bundle = "/Applications/Thorium Inbox.app";
            string file = Path.Combine(home.FullName, "Library", "LaunchAgents", "io.github.katus4.thoriuminbox.plist");
            LoginStartupFiles.SetMacEnabled(home.FullName, bundle, enabled: true);
            var plist = XDocument.Load(file);
            Assert.Equal(new[] { "/usr/bin/open", "-a", bundle, "--args", "--background" },
                plist.Descendants("array").Elements("string").Select(node => node.Value));
            Assert.Contains(plist.Descendants("key"), key => key.Value == "RunAtLoad" && key.ElementsAfterSelf().First().Name == "true");
            Assert.True(LoginStartupFiles.IsMacEnabled(home.FullName));

            LoginStartupFiles.SetMacEnabled(home.FullName, bundle, enabled: false);
            Assert.False(File.Exists(file));
        }
        finally { home.Delete(recursive: true); }
    }
}
