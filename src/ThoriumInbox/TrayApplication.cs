using System.Diagnostics;
using ThoriumInbox.Core;

namespace ThoriumInbox;

internal sealed class TrayApplication : ApplicationContext
{
    private readonly SettingsWindow window;
    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip menu;
    private readonly Icon? customIcon;
    private readonly ReleaseNotifications updates;
    private InboxWatcher? watcher;
    private string inbox = AppLocations.ReadInbox();
    private InboxActivity? lastLogged;
    private bool changingFolder;
    private bool quitting;

    public TrayApplication(bool background)
    {
        customIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        Icon icon = customIcon ?? SystemIcons.Application;
        window = new SettingsWindow(icon, ChangeFolder, OpenInbox, OpenEpubs, ViewActivity);
        _ = window.Handle;
        updates = new ReleaseNotifications(window, release => OpenPath(() => release.Page.AbsoluteUri));

        menu = new ContextMenuStrip();
        menu.Items.Add("Open inbox", null, (_, _) => OpenInbox());
        menu.Items.Add("Open EPUBs", null, (_, _) => OpenEpubs());
        menu.Items.Add("View activity", null, (_, _) => ViewActivity());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings…", null, (_, _) => ShowWindow());
        menu.Items.Add("Check for updates…", null, async (_, _) => await updates.CheckNowAsync());
        menu.Items.Add("Quit", null, async (_, _) => await QuitAsync());

        tray = new NotifyIcon { Icon = icon, Text = "Thorium Inbox", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ShowWindow();

        window.SetFolder(inbox);
        TryStartWatcher(inbox);
        if (!background)
            ShowWindow();
    }

    public void RequestOpen() => Post(ShowWindow);

    private void ShowWindow()
    {
        window.Show();
        window.WindowState = FormWindowState.Normal;
        window.Activate();
    }

    private void StartWatcher(string path)
    {
        AppLocations.ValidateInbox(path);
        Directory.CreateDirectory(AppLocations.AppData);
        var processor = new InboxProcessor(path, AppLocations.Epubs, AppLocations.History,
            ThoriumImporter.ImportAsync, update => Post(() => Display(update)));
        watcher = new InboxWatcher(path, processor, update => Post(() => Display(update)));
        Display(new InboxActivity("Watching for PDFs and EPUBs."));
    }

    private void TryStartWatcher(string path)
    {
        try { StartWatcher(path); }
        catch (Exception exception)
        {
            Display(new InboxActivity($"Could not start watching the folder. {exception.Message}", IsError: true));
        }
    }

    private async void ChangeFolder()
    {
        if (changingFolder || quitting)
            return;
        using var picker = new FolderBrowserDialog
        {
            InitialDirectory = inbox,
            Description = "Choose the folder Thorium Inbox should watch",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        };
        if (picker.ShowDialog(window) != DialogResult.OK)
            return;

        changingFolder = true;
        try
        {
            string requested = AppLocations.ValidateInbox(picker.SelectedPath);
            if (watcher is not null)
            {
                await watcher.DisposeAsync();
                watcher = null;
            }

            StartWatcher(requested);
            AppLocations.WriteInbox(requested);
            inbox = requested;
            window.SetFolder(inbox);
        }
        catch (Exception exception)
        {
            if (watcher is not null)
            {
                await watcher.DisposeAsync();
                watcher = null;
            }
            TryStartWatcher(inbox);
            Display(new InboxActivity($"Could not change the watched folder. {exception.Message}", IsError: true));
        }
        finally { changingFolder = false; }
    }

    private void OpenInbox() => OpenPath(() => Directory.CreateDirectory(inbox).FullName);

    private void OpenEpubs() => OpenPath(() => Directory.CreateDirectory(AppLocations.Epubs).FullName);

    private void ViewActivity() => OpenPath(() =>
    {
        Directory.CreateDirectory(AppLocations.AppData);
        using (File.Open(AppLocations.Activity, FileMode.OpenOrCreate, FileAccess.ReadWrite)) { }
        return AppLocations.Activity;
    });

    private void OpenPath(Func<string> getPath)
    {
        try { Process.Start(new ProcessStartInfo(getPath()) { UseShellExecute = true })?.Dispose(); }
        catch (Exception exception)
        {
            MessageBox.Show(window, exception.Message, "Thorium Inbox", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Display(InboxActivity update)
    {
        if (quitting)
            return;
        window.SetActivity(update);
        tray.Text = update.IsError ? "Thorium Inbox needs attention" : "Thorium Inbox";
        if (update.IsProgress || lastLogged == update)
            return;
        lastLogged = update;

        try
        {
            Directory.CreateDirectory(AppLocations.AppData);
            File.AppendAllText(AppLocations.Activity, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {update.Message}{Environment.NewLine}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        if (update.IsError && !window.Visible)
            tray.ShowBalloonTip(5000, "Thorium Inbox", update.Message, ToolTipIcon.Warning);
    }

    private void Post(Action action)
    {
        if (window.IsDisposed || quitting)
            return;
        try { window.BeginInvoke(action); }
        catch (InvalidOperationException) { }
    }

    private async Task QuitAsync()
    {
        if (quitting || changingFolder)
            return;
        quitting = true;
        tray.Visible = false;
        try
        {
            if (watcher is not null)
                await watcher.DisposeAsync();
        }
        finally { ExitThread(); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            updates.Dispose();
            tray.Dispose();
            menu.Dispose();
            window.Dispose();
            customIcon?.Dispose();
        }
        base.Dispose(disposing);
    }
}
