using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace ThoriumInbox;

internal sealed class InboxApplication : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            bool background = lifetime.Args?.Contains("--background", StringComparer.OrdinalIgnoreCase) == true;
            var desktop = new InboxDesktop(this, lifetime);
            lifetime.Exit += (_, _) => desktop.Dispose();
            desktop.Start(background);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
