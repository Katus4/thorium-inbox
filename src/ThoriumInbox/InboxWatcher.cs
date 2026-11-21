using ThoriumInbox.Core;

namespace ThoriumInbox;

internal sealed class InboxWatcher : IAsyncDisposable
{
    private readonly FileSystemWatcher watcher;
    private readonly SemaphoreSlim change = new(0, 1);
    private readonly CancellationTokenSource stop = new();
    private readonly Task loop;

    public InboxWatcher(string folder, InboxProcessor processor, Action<InboxActivity> report)
    {
        watcher = new FileSystemWatcher(folder)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
        };
        watcher.Created += OnChanged;
        watcher.Changed += OnChanged;
        watcher.Deleted += OnChanged;
        watcher.Renamed += OnChanged;
        watcher.Error += (_, _) => Signal();
        watcher.EnableRaisingEvents = true;
        loop = Task.Run(() => WatchAsync(processor, report, stop.Token));
    }

    private void OnChanged(object? sender, FileSystemEventArgs args) => Signal();

    private void Signal()
    {
        try { change.Release(); }
        catch (SemaphoreFullException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task WatchAsync(InboxProcessor processor, Action<InboxActivity> report, CancellationToken token)
    {
        string? lastScanError = null;
        try
        {
            while (!token.IsCancellationRequested)
            {
                bool checkSoon = false;
                try
                {
                    checkSoon = await processor.ScanAsync(token).ConfigureAwait(false);
                    if (lastScanError is not null)
                        report(new InboxActivity("Watching for PDFs and EPUBs."));
                    lastScanError = null;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    if (exception.Message != lastScanError)
                        report(new InboxActivity($"Could not scan the inbox. {exception.Message}", IsError: true));
                    lastScanError = exception.Message;
                }

                // Periodic scans also pick up changes if Windows drops a file-system event.
                TimeSpan delay = TimeSpan.FromSeconds(checkSoon ? 2 : 30);
                await change.WaitAsync(delay, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        watcher.Dispose();
        stop.Cancel();
        try { await loop.ConfigureAwait(false); }
        finally
        {
            change.Dispose();
            stop.Dispose();
        }
    }
}
