using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ThoriumInbox.Core;

public static class ThoriumLibraryReceipt
{
    private const string PublicationPath = "/publication/db";

    public static string Fingerprint(string epubPath)
    {
        using var archive = ZipFile.OpenRead(epubPath);
        // Thorium identifies EPUBs by the ordered CRCs of their ZIP entries.
        // License metadata may change after a protected book is imported.
        var checksums = archive.Entries
            .Where(entry => entry.FullName != "META-INF/license.lcpl")
            .Select(entry => entry.Crc32.ToString("x", CultureInfo.InvariantCulture));
        string joined = "|" + string.Concat(checksums.Select(checksum => checksum + "|"));
        return Convert.ToHexStringLower(SHA1.HashData(Encoding.ASCII.GetBytes(joined)));
    }

    public static bool HasImport(string profile, string fingerprint)
    {
        string config = Path.Combine(profile, "config-data-json");
        var matches = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in new[] { "state.json", "state.runtime.json" })
        {
            try
            {
                using var stream = ReadShared(Path.Combine(config, file));
                using var state = JsonDocument.Parse(stream);
                if (!state.RootElement.TryGetProperty("publication", out var publication)
                    || !publication.TryGetProperty("db", out var database)
                    || database.ValueKind != JsonValueKind.Object)
                    continue;
                ReadMatches(database, fingerprint, matches);
                break;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                // The reader may be creating or replacing its snapshot.
            }
        }

        try
        {
            // While Thorium is open, new library records arrive as comma-terminated JSON patches.
            using var patches = new StreamReader(ReadShared(Path.Combine(config, "state.patch.json")));
            while (patches.ReadLine() is { } line)
            {
                string patch = line.Trim().TrimEnd(',');
                if (patch.Length == 0)
                    continue;
                try
                {
                    using var parsed = JsonDocument.Parse(patch);
                    ApplyPublicationPatch(parsed.RootElement, fingerprint, matches);
                }
                catch (JsonException) { }
                catch (InvalidOperationException) { }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }

        return matches.Count != 0;
    }

    private static FileStream ReadShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private static void ReadMatches(JsonElement database, string fingerprint, HashSet<string> matches)
    {
        foreach (var record in database.EnumerateObject())
            if (MatchesRecord(record.Value, fingerprint))
                matches.Add(record.Name);
    }

    private static bool MatchesRecord(JsonElement record, string fingerprint) =>
        record.ValueKind == JsonValueKind.Object && record.TryGetProperty("hash", out var hash)
        && hash.ValueKind == JsonValueKind.String && hash.GetString() == fingerprint;

    private static void ApplyPublicationPatch(JsonElement patch, string fingerprint, HashSet<string> matches)
    {
        if (!patch.TryGetProperty("path", out var pathValue) || pathValue.GetString() is not string path)
            return;
        if (path == PublicationPath)
        {
            matches.Clear();
            if (patch.TryGetProperty("value", out var database) && database.ValueKind == JsonValueKind.Object)
                ReadMatches(database, fingerprint, matches);
            return;
        }
        if (!path.StartsWith(PublicationPath + "/", StringComparison.Ordinal))
            return;

        string[] parts = path[(PublicationPath.Length + 1)..].Split('/');
        string id = parts[0];
        bool isRecord = parts.Length == 1;
        if (!isRecord && (parts.Length != 2 || parts[1] != "hash"))
            return;

        matches.Remove(id);
        if (patch.TryGetProperty("value", out var value)
            && (isRecord ? MatchesRecord(value, fingerprint)
                : value.ValueKind == JsonValueKind.String && value.GetString() == fingerprint))
            matches.Add(id);
    }
}
