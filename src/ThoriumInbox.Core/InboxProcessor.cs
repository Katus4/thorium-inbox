using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PDFtoImage.Exceptions;

namespace ThoriumInbox.Core;

public sealed record InboxActivity(string Message, bool IsError = false, bool IsProgress = false);

public sealed class InboxProcessor
{
    private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private readonly string inboxDirectory;
    private readonly string outputDirectory;
    private readonly string historyFile;
    private readonly Func<string, CancellationToken, Task> import;
    private readonly Action<InboxActivity> report;
    private readonly TimeProvider clock;
    private readonly HashSet<string> imported;
    private readonly Dictionary<string, FileObservation> observations = new(PathComparer);

    public InboxProcessor(string inboxDirectory, string outputDirectory, string historyFile,
        Func<string, CancellationToken, Task> import, Action<InboxActivity> report, TimeProvider? clock = null)
    {
        this.inboxDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(inboxDirectory));
        this.outputDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory));
        this.historyFile = historyFile;
        this.import = import;
        this.report = report;
        this.clock = clock ?? TimeProvider.System;
        if (PathComparer.Equals(this.inboxDirectory, this.outputDirectory))
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
        var currentPaths = files.ToHashSet(PathComparer);
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
            // Windows can deny writers while allowing readers. Unix provides a best-effort exclusive lock.
            FileShare sharing = OperatingSystem.IsWindows() ? FileShare.Read : FileShare.None;
            string hash;
            string epubPath = path;
            using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, sharing))
            {
                fileOpened = true;
                if (!MatchesObservation(source, observation))
                    return true;
                hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(source, cancellationToken).ConfigureAwait(false));
                if (!MatchesObservation(source, observation))
                    return true;

                if (imported.Contains(hash))
                {
                    observation.Done = true;
                    return false;
                }

                if (isPdf)
                {
                    report(new InboxActivity($"Converting {name}", IsProgress: true));
                    source.Position = 0;
                    string? converted = CreateEpub(source, Path.GetFileNameWithoutExtension(path), hash, observation, cancellationToken);
                    if (converted is null)
                        return true;
                    epubPath = converted;
                }

                if (!MatchesObservation(source, observation))
                    return true;
            }

            // Release the input handle before an importer opens the original EPUB.
            report(new InboxActivity($"Importing {Path.GetFileName(epubPath)}", IsProgress: true));
            await import(epubPath, cancellationToken).ConfigureAwait(false);
            SaveReceipt(hash);
            observation.Done = true;
            report(new InboxActivity($"Added {name} to Thorium."));
        }
        catch (IOException exception) when (!fileOpened && IsOpeningConflict(exception))
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

    private static bool MatchesObservation(FileStream source, FileObservation observation) =>
        source.Length == observation.Stamp.Length && File.GetLastWriteTimeUtc(source.Name) == observation.Stamp.LastWrite;

    private static bool IsOpeningConflict(IOException exception)
    {
        int code = exception.HResult & 0xffff;
        // .NET reports the native EWOULDBLOCK errno for Unix advisory file locks.
        return code is 2 or 3 or 32 or 33
            || (OperatingSystem.IsLinux() && code == 11)
            || (OperatingSystem.IsMacOS() && code == 35);
    }

    private string? CreateEpub(FileStream source, string title, string hash, FileObservation observation,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        string safeTitle = FitOutputStem(title);
        string destination = Path.Combine(outputDirectory, $"{safeTitle} - {hash[..12]}.epub");
        if (File.Exists(destination))
            return destination;

        string partial = destination + ".tmp";
        try
        {
            using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
                PdfEpubConverter.Convert(source, title, hash, output, cancellationToken);
            if (!MatchesObservation(source, observation))
                return null;
            File.Move(partial, destination);
            return destination;
        }
        finally
        {
            File.Delete(partial);
        }
    }

    private static string FitOutputStem(string title)
    {
        // Leave room for the hash suffix and .epub.tmp within a 255-byte filename component.
        const int maxStemBytes = 230;
        const int maxStemCharacters = 120;
        int bytes = 0;
        int characters = 0;
        foreach (var rune in title.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > maxStemBytes
                || characters + rune.Utf16SequenceLength > maxStemCharacters)
                break;
            bytes += rune.Utf8SequenceLength;
            characters += rune.Utf16SequenceLength;
        }

        return title[..characters];
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
