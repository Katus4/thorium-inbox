using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace ThoriumInbox;

internal sealed class DesktopTray : IDisposable
{
    private readonly Application application;
    private readonly TrayIcon icon;

    public DesktopTray(Application application, WindowIcon windowIcon, Action open, Action openInbox, Action openEpubs,
        Action viewActivity, Action checkUpdates, Action quit)
    {
        this.application = application;
        var menu = new NativeMenu();
        Add(menu, "Open settings", open);
        Add(menu, "Open inbox", openInbox);
        Add(menu, "Open EPUBs", openEpubs);
        Add(menu, "View activity", viewActivity);
        menu.Items.Add(new NativeMenuItemSeparator());
        Add(menu, "Check for updates…", checkUpdates);
        Add(menu, "Quit", quit);
        icon = new TrayIcon
        {
            Icon = windowIcon,
            ToolTipText = "Thorium Inbox",
            IsVisible = true,
            Menu = menu,
            Command = new TrayCommand(open),
        };
        TrayIcon.SetIcons(application, new TrayIcons { icon });
    }

    public void SetNeedsAttention(bool needsAttention) =>
        icon.ToolTipText = needsAttention ? "Thorium Inbox needs attention" : "Thorium Inbox";

    private static void Add(NativeMenu menu, string title, Action action) =>
        menu.Items.Add(new NativeMenuItem(title) { Command = new TrayCommand(action) });

    public void Dispose()
    {
        TrayIcon.SetIcons(application, null);
        icon.Dispose();
    }

    private sealed class TrayCommand(Action action) : ICommand
    {
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
