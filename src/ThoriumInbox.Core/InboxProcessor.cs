using System.Security.Cryptography;
using System.Text.Json;
using PDFtoImage.Exceptions;

namespace ThoriumInbox.Core;

public sealed record InboxActivity(string Message, bool IsError = false, bool IsProgress = false);

public sealed class InboxProcessor
{
    private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);
    private readonly string inboxDirectory;
    private readonly string outputDirectory;
    private readonly string historyFile;
    private readonly Func<string, CancellationToken, Task> import;
    private readonly Action<InboxActivity> report;
    private readonly TimeProvider clock;
    private readonly HashSet<string> imported;
    private readonly Dictionary<string, FileObservation> observations = new(StringComparer.OrdinalIgnoreCase);

    public InboxProcessor(string inboxDirectory, string outputDirectory, string historyFile,
        Func<string, CancellationToken, Task> import, Action<InboxActivity> report, TimeProvider? clock = null)
    {
        this.inboxDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(inboxDirectory));
        this.outputDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory));
        this.historyFile = historyFile;
        this.import = import;
        this.report = report;
        this.clock = clock ?? TimeProvider.System;
        if (StringComparer.OrdinalIgnoreCase.Equals(this.inboxDirectory, this.outputDirectory))
            throw new ArgumentException("Choose an inbox separate from the EPUB output folder.", nameof(inboxDirectory));

        imported = File.Exists(historyFile)
            ? JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(historyFile))
                ?? throw new InvalidDataException("The import history could not be read.")
            : [];
    }

    // Called by one worker. A true result means a file should be checked again soon.
    public async Task<bool> ScanAsync(CancellationToken cancellationToken = default)
    {
        bool checkSoon = false;
        string[] files = Directory.GetFiles(inboxDirectory);
        var currentPaths = files.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var removed in observations.Keys.Where(path => !currentPaths.Contains(path)).ToArray())
            observations.Remove(removed);

        foreach (string path in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string extension = Path.GetExtension(path);
            bool isPdf = extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);
            if (!isPdf && !extension.Equals(".epub", StringComparison.OrdinalIgnoreCase))
                continue;

            var file = new FileInfo(path);
            if (!file.Exists)
                continue;

            var stamp = (file.Length, file.LastWriteTimeUtc);
            var now = clock.GetUtcNow();
            if (!observations.TryGetValue(path, out var observation) || observation.Stamp != stamp)
            {
                observations[path] = new FileObservation(stamp, now + SettleTime);
                checkSoon = true;
                continue;
            }

            if (observation.Done)
                continue;
            if (now < observation.TryAfter)
            {
                checkSoon |= observation.TryAfter - now <= SettleTime;
                continue;
            }

            checkSoon |= await ProcessFileAsync(path, isPdf, observation, cancellationToken).ConfigureAwait(false);
        }

        return checkSoon;
    }

    private async Task<bool> ProcessFileAsync(string path, bool isPdf, FileObservation observation,
        CancellationToken cancellationToken)
    {
        string name = Path.GetFileName(path);
        bool fileOpened = false;
        try
        {
            // Windows rejects this sharing mode while a downloader still has the file open for writing.
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            fileOpened = true;
            if (source.Length != observation.Stamp.Length || File.GetLastWriteTimeUtc(path) != observation.Stamp.LastWrite)
                return true;

            string hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(source, cancellationToken).ConfigureAwait(false));
            if (imported.Contains(hash))
            {
                observation.Done = true;
                return false;
            }

            string epubPath = path;
            if (isPdf)
            {
                report(new InboxActivity($"Converting {name}", IsProgress: true));
                source.Position = 0;
                epubPath = CreateEpub(source, Path.GetFileNameWithoutExtension(path), hash, cancellationToken);
            }

            report(new InboxActivity($"Importing {Path.GetFileName(epubPath)}", IsProgress: true));
            await import(epubPath, cancellationToken).ConfigureAwait(false);
            SaveReceipt(hash);
            observation.Done = true;
            report(new InboxActivity($"Added {name} to Thorium."));
        }
        catch (IOException exception) when (!fileOpened && (exception.HResult & 0xffff) is 2 or 3 or 32 or 33)
        {
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            observation.TryAfter = clock.GetUtcNow() + RetryDelay;
            string reason = exception is PdfPasswordProtectedException
                ? "This PDF requires a password. Save an unlocked copy to import it."
                : exception.Message;
            report(new InboxActivity($"{name}: {reason}", IsError: true));
        }

        return false;
    }

    private string CreateEpub(Stream source, string title, string hash, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        string safeTitle = title.Length > 120 ? title[..120] : title;
        string destination = Path.Combine(outputDirectory, $"{safeTitle} - {hash[..12]}.epub");
        if (File.Exists(destination))
            return destination;

        string partial = destination + ".tmp";
        try
        {
            using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
                PdfEpubConverter.Convert(source, title, hash, output, cancellationToken);
            File.Move(partial, destination);
            return destination;
        }
        finally
        {
            File.Delete(partial);
        }
    }

    private void SaveReceipt(string hash)
    {
        string partial = historyFile + ".tmp";
        using (var output = File.Create(partial))
            JsonSerializer.Serialize(output, imported.Append(hash));
        File.Move(partial, historyFile, overwrite: true);
        imported.Add(hash);
    }

    private sealed class FileObservation((long Length, DateTime LastWrite) stamp, DateTimeOffset tryAfter)
    {
        public (long Length, DateTime LastWrite) Stamp { get; } = stamp;
        public DateTimeOffset TryAfter { get; set; } = tryAfter;
        public bool Done { get; set; }
    }
}
