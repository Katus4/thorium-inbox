using System.Reflection;
using System.Text.Json;
using ThoriumInbox.Core;

namespace ThoriumInbox;

internal sealed class ReleaseNotifications : IDisposable
{
    private const string Installer = "ThoriumInbox-Setup-x64.exe";
    private readonly Form owner;
    private readonly Action<GitHubUpdate> openRelease;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 30_000 };
    private readonly CancellationTokenSource shutdown = new();
    private readonly HttpClient client = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
        MaxResponseContentBufferSize = 1_048_576,
    };
    private readonly Version current = Assembly.GetExecutingAssembly().GetName().Version!;
    private bool checking;
    private bool disposed;

    public ReleaseNotifications(Form owner, Action<GitHubUpdate> openRelease)
    {
        this.owner = owner;
        this.openRelease = openRelease;
        timer.Tick += async (_, _) =>
        {
            timer.Interval = (int)TimeSpan.FromDays(1).TotalMilliseconds;
            await CheckAsync(manual: false);
        };
        timer.Start();
    }

    public Task CheckNowAsync() => CheckAsync(manual: true);

    private async Task CheckAsync(bool manual)
    {
        if (disposed || checking)
            return;

        checking = true;
        try
        {
            var update = await new GitHubReleaseFeed(client).FindUpdateAsync(current, Installer, shutdown.Token);
            if (disposed || owner.IsDisposed)
                return;
            if (update is null)
            {
                if (manual)
                    Inform("No newer Windows release is available.", MessageBoxIcon.Information);
                return;
            }

            using var dialog = new ReleaseUpdateDialog(update) { Icon = owner.Icon };
            if ((owner.Visible ? dialog.ShowDialog(owner) : dialog.ShowDialog()) == DialogResult.OK && !disposed)
                openRelease(update);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            if (manual && !disposed && !owner.IsDisposed)
                Inform("Could not check GitHub for updates. Try again later.", MessageBoxIcon.Warning);
        }
        finally { checking = false; }
    }

    private void Inform(string text, MessageBoxIcon icon)
    {
        if (owner.Visible)
            MessageBox.Show(owner, text, "Thorium Inbox updates", MessageBoxButtons.OK, icon);
        else
            MessageBox.Show(text, "Thorium Inbox updates", MessageBoxButtons.OK, icon);
    }

    public void Dispose()
    {
        disposed = true;
        timer.Dispose();
        shutdown.Cancel();
        client.Dispose();
        shutdown.Dispose();
    }
}
