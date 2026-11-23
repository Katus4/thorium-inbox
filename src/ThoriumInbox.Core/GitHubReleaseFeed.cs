using System.Net;
using System.Text.Json;

namespace ThoriumInbox.Core;

public sealed record GitHubUpdate(Version Version, string Notes, Uri Page, bool HasSignedManifest = false);

public sealed class GitHubReleaseFeed(HttpClient client)
{
    public const string Repository = "Katus4/thorium-inbox";

    public async Task<GitHubUpdate?> FindUpdateAsync(Version installedVersion, string installerName,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
        request.Headers.UserAgent.ParseAdd("ThoriumInbox");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();

        using var data = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
        var release = data.RootElement;
        if (release.ValueKind != JsonValueKind.Object)
            return null;
        if (!release.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False
            || !release.TryGetProperty("prerelease", out var prerelease) || prerelease.ValueKind != JsonValueKind.False)
            return null;

        string? tag = ReadText(release, "tag_name");
        if (tag is null || !tag.StartsWith('v') || !Version.TryParse(tag[1..], out var availableVersion)
            || availableVersion.Build < 0 || availableVersion.Revision >= 0 || tag != $"v{availableVersion}")
            return null;
        var installed = new Version(installedVersion.Major, installedVersion.Minor, Math.Max(0, installedVersion.Build));
        if (availableVersion <= installed)
            return null;

        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;
        var uploaded = assets.EnumerateArray()
            .Where(asset => ReadText(asset, "state") == "uploaded")
            .Select(asset => ReadText(asset, "name"))
            .ToHashSet(StringComparer.Ordinal);
        if (!uploaded.Contains(installerName))
            return null;

        return new GitHubUpdate(availableVersion, ReadText(release, "body") ?? "",
            new Uri($"https://github.com/{Repository}/releases/tag/{tag}"),
            uploaded.Contains(ReleaseAssets.Manifest) && uploaded.Contains(ReleaseAssets.ManifestSignature));
    }

    private static string? ReadText(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
