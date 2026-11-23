using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Threading;
using ThoriumInbox.Core;

namespace ThoriumInbox;

internal sealed class ReleaseNotifications : IDisposable
{
    private readonly SettingsWindow owner;
    private readonly Action showWindow;
    private readonly Func<Task> quit;
    private readonly CancellationTokenSource shutdown = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly HttpClient client = new()
    {
        Timeout = TimeSpan.FromSeconds(20),
        MaxResponseContentBufferSize = 1_048_576,
    };
    private readonly string? key = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == "ThoriumInbox.UpdatePublicKey")?.Value;
    private bool checking;
    private bool disposed;

    public ReleaseNotifications(SettingsWindow owner, Action showWindow, Func<Task> quit)
    {
        this.owner = owner;
        this.showWindow = showWindow;
        this.quit = quit;
        timer.Tick += async (_, _) =>
        {
            timer.Interval = TimeSpan.FromDays(1);
            await CheckAsync(manual: false);
        };
        timer.Start();
    }

    public Task CheckNowAsync() => CheckAsync(manual: true);

#if THORIUM_TEST_BUILD
    public async Task PreviewAsync()
    {
        if (disposed || checking)
            return;

        checking = true;
        try
        {
            var release = new GitHubUpdate(new Version(Program.Version.Major, Program.Version.Minor, Program.Version.Build + 1),
                "Test preview. This dialog uses sample release notes.\n\nNew features\n• Open the watched folder from settings.\n\nFixes\n• Keep the first page readable beside a large foldout.",
                new Uri($"https://github.com/{GitHubReleaseFeed.Repository}/releases"), HasSignedManifest: true);
            bool accepted = await new ReleaseUpdateDialog(release, canInstall: true).AskAsync(owner);
            if (!disposed)
                ShowStatus(accepted ? "Preview complete. No installer was downloaded." : "Preview dismissed.");
        }
        finally { checking = false; }
    }
#endif

    private async Task CheckAsync(bool manual)
    {
        if (disposed || checking)
            return;
        if (ReleaseAssets.CurrentInstaller() is not { } installerName)
        {
            if (manual)
                ShowStatus("GitHub installer updates are unavailable for this platform.");
            return;
        }

        checking = true;
        try
        {
            if (manual)
                ShowStatus("Checking GitHub for updates…");

            var release = await new GitHubReleaseFeed(client).FindUpdateAsync(Program.Version, installerName, shutdown.Token);
            if (disposed)
                return;
            if (release is null)
            {
                if (manual)
                    ShowStatus("No newer release is available for this platform.");
                return;
            }

            owner.SetUpdateStatus(null);
            bool canInstall = release.HasSignedManifest && !string.IsNullOrWhiteSpace(key);
            var dialog = new ReleaseUpdateDialog(release, canInstall);
            if (!await dialog.AskAsync(owner) || disposed)
                return;
            if (canInstall)
                await InstallAsync(release, installerName);
            else
                Process.Start(new ProcessStartInfo(release.Page.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or OperationCanceledException
            or IOException or InvalidDataException or UnauthorizedAccessException or CryptographicException or FormatException
            or System.ComponentModel.Win32Exception)
        {
            if (!disposed && (manual || owner.IsVisible))
                ShowStatus($"Could not complete the update. {exception.Message}");
        }
        finally { checking = false; }
    }

    private async Task InstallAsync(GitHubUpdate release, string installerName)
    {
        ShowStatus($"Downloading and verifying version {release.Version}…");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
        deadline.CancelAfter(TimeSpan.FromMinutes(10));
        var releases = new GitHubReleaseInstaller(client, key!);
        string root = Path.Combine(AppLocations.AppData, "updates");
        string installer = await releases.DownloadAsync(release.Version, installerName, root, deadline.Token);
        if (disposed)
            return;
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            var launcher = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "/usr/bin/open" : "xdg-open")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            launcher.ArgumentList.Add(installer);
            using var process = Process.Start(launcher) ?? throw new IOException("Could not open the platform installer.");
            var exited = process.WaitForExitAsync(deadline.Token);
            await Task.WhenAny(exited, Task.Delay(TimeSpan.FromSeconds(1), deadline.Token));
            deadline.Token.ThrowIfCancellationRequested();
            if (process.HasExited && process.ExitCode != 0)
            {
                ShowStatus($"The verified update is saved at {installer}. Open it with your package installer.");
                return;
            }
        }
        else
        {
            Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true })?.Dispose();
        }
        await quit();
    }

    private void ShowStatus(string message)
    {
        showWindow();
        owner.SetUpdateStatus(message);
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        timer.Stop();
        shutdown.Cancel();
        client.Dispose();
        shutdown.Dispose();
    }
}
