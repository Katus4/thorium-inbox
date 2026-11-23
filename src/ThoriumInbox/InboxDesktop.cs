using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ThoriumInbox.Core;

namespace ThoriumInbox;

internal sealed class InboxDesktop : IDisposable
{
    private readonly IClassicDesktopStyleApplicationLifetime lifetime;
    private readonly SettingsWindow window;
    private readonly DesktopTray tray;
    private readonly ReleaseNotifications updates;
    private readonly IActivatableLifetime? activation;
    private InboxWatcher? watcher;
    private FileSystemWatcher? openSignal;
    private InboxActivity? lastLogged;
    private string inbox = AppLocations.ReadInbox();
    private bool changingFolder;
    private bool quitting;
    private bool disposed;

    public InboxDesktop(Application application, IClassicDesktopStyleApplicationLifetime lifetime)
    {
        this.lifetime = lifetime;
        using var iconStream = AssetLoader.Open(new Uri("avares://ThoriumInbox/Assets/inbox.ico"));
        var icon = new WindowIcon(iconStream);
        window = new SettingsWindow(icon, ChangeFolder, ChooseReader, OpenInbox, OpenEpubs, ViewActivity,
            ChangeStartup, async () => await CheckUpdatesAsync(), async () => await QuitAsync());
        window.Closing += (_, args) =>
        {
            if (quitting || args.CloseReason != WindowCloseReason.WindowClosing)
                return;
            args.Cancel = true;
            window.Hide();
        };
        updates = new ReleaseNotifications(window, ShowWindow, QuitAsync);
#if THORIUM_TEST_BUILD
        window.AddUpdatePreview(async () => await updates.PreviewAsync());
#endif
        tray = new DesktopTray(application, icon, ShowWindow, OpenInbox, OpenEpubs, ViewActivity,
            async () => await CheckUpdatesAsync(), async () => await QuitAsync());
        if (OperatingSystem.IsMacOS())
        {
            activation = application.TryGetFeature<IActivatableLifetime>();
            if (activation is not null)
                activation.Activated += ReopenSettings;
        }
    }

    public void Start(bool background)
    {
        window.SetFolder(inbox);
        RefreshReader();
        TryStartWatcher(inbox);
        try
        {
            window.SetStartup(LoginStartup.IsAvailable, LoginStartup.Initialize());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Display(new InboxActivity($"Could not configure login startup. {exception.Message}", IsError: true));
        }
        try
        {
            StartWindowRequests();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Display(new InboxActivity($"Could not enable reopening settings from the launcher. {exception.Message}", IsError: true));
        }

        if (!background)
            ShowWindow();
    }

    private void ShowWindow()
    {
        if (quitting)
            return;
        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
    }

    private Task CheckUpdatesAsync() => updates.CheckNowAsync();

    private void ReopenSettings(object? sender, ActivatedEventArgs args)
    {
        if (args.Kind == ActivationKind.Reopen)
            ShowWindow();
    }

    private void StartWindowRequests()
    {
        Directory.CreateDirectory(AppLocations.AppData);
        openSignal = new FileSystemWatcher(AppLocations.AppData, Path.GetFileName(AppLocations.OpenWindowSignal));
        openSignal.Created += (_, _) => Dispatcher.UIThread.Post(ShowRequestedWindow);
        openSignal.Changed += (_, _) => Dispatcher.UIThread.Post(ShowRequestedWindow);
        openSignal.EnableRaisingEvents = true;
        ShowRequestedWindow();
    }

    private void ShowRequestedWindow()
    {
        if (!File.Exists(AppLocations.OpenWindowSignal))
            return;
        try { File.Delete(AppLocations.OpenWindowSignal); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        ShowWindow();
    }

    private void StartWatcher(string path)
    {
        AppLocations.ValidateInbox(path);
        Directory.CreateDirectory(AppLocations.AppData);
        var processor = new InboxProcessor(path, AppLocations.Epubs, AppLocations.History,
            ThoriumImporter.ImportAsync, PostActivity);
        watcher = new InboxWatcher(path, processor, PostActivity);
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

        changingFolder = true;
        string previous = inbox;
        bool changedWatcher = false;
        try
        {
            var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose the watched folder",
                AllowMultiple = false,
            });
            if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } path)
                return;
            string requested = AppLocations.ValidateInbox(path);
            await StopWatcherAsync();
            changedWatcher = true;
            StartWatcher(requested);
            AppLocations.WriteInbox(requested);
            inbox = requested;
            window.SetFolder(inbox);
        }
        catch (Exception exception)
        {
            if (changedWatcher)
            {
                await StopWatcherAsync();
                TryStartWatcher(previous);
            }
            Display(new InboxActivity($"Could not change the watched folder. {exception.Message}", IsError: true));
        }
        finally { changingFolder = false; }
    }

    private async void ChooseReader()
    {
        try
        {
            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose Thorium Reader",
                AllowMultiple = false,
            });
            if (files.FirstOrDefault()?.TryGetLocalPath() is not { } selected)
                return;

            if (ThoriumImporter.ResolveSelection(selected) is not { } executable)
            {
                Display(new InboxActivity("Choose the Thorium executable, AppImage, or Mac app bundle.", IsError: true));
                return;
            }
            AppLocations.WritePreference(ThoriumImporter.ReaderPreference, executable);
            RefreshReader();
        }
        catch (Exception exception)
        {
            Display(new InboxActivity($"Could not set the Thorium executable. {exception.Message}", IsError: true));
        }
    }

    private void RefreshReader() => window.SetReader(ThoriumImporter.FindDesktop());

    private void ChangeStartup(bool enabled)
    {
        try { LoginStartup.SetEnabled(enabled); }
        catch (Exception exception)
        {
            window.SetStartup(LoginStartup.IsAvailable, !enabled);
            Display(new InboxActivity($"Could not change login startup. {exception.Message}", IsError: true));
        }
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
            ShowWindow();
            Display(new InboxActivity($"Could not open the requested folder or file. {exception.Message}", IsError: true));
        }
    }

    private void PostActivity(InboxActivity update) => Dispatcher.UIThread.Post(() => Display(update));

    private void Display(InboxActivity update)
    {
        if (quitting)
            return;
        window.SetActivity(update.Message);
        tray.SetNeedsAttention(update.IsError);
        if (update.IsProgress || lastLogged == update)
            return;
        lastLogged = update;

        try
        {
            Directory.CreateDirectory(AppLocations.AppData);
            File.AppendAllText(AppLocations.Activity, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {update.Message}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task StopWatcherAsync()
    {
        var active = watcher;
        watcher = null;
        if (active is not null)
            await active.DisposeAsync();
    }

    private async Task QuitAsync()
    {
        if (quitting)
            return;
        quitting = true;
        try { await StopWatcherAsync(); }
        finally { lifetime.Shutdown(); }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        quitting = true;
        if (activation is not null)
            activation.Activated -= ReopenSettings;
        openSignal?.Dispose();
        updates.Dispose();
        tray.Dispose();
        StopWatcherAsync().GetAwaiter().GetResult();
    }
}
