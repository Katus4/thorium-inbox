using System.Security.Cryptography;
using System.Text.Json;

namespace ThoriumInbox.Core;

public sealed class GitHubReleaseInstaller(HttpClient client, string publicKey)
{
    public async Task<string> DownloadAsync(Version version, string installerName, string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKey);
        if (!ReleaseAssets.IsInstaller(installerName))
            throw new ArgumentException("Unknown installer name.", nameof(installerName));

        string tag = $"v{version.ToString(3)}";
        string prefix = $"https://github.com/{GitHubReleaseFeed.Repository}/releases/download/{tag}/";
        byte[] manifest = await ReadSmallAssetAsync(prefix + ReleaseAssets.Manifest, 64 * 1024, cancellationToken).ConfigureAwait(false);
        byte[] signature = await ReadSmallAssetAsync(prefix + ReleaseAssets.ManifestSignature, 8192, cancellationToken).ConfigureAwait(false);
        byte[] expected = ReadVerifiedDigest(manifest, signature, tag, installerName);

        string directory = Path.Combine(destinationDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string installer = Path.Combine(directory, installerName);
        try
        {
            using var response = await client.GetAsync(prefix + installerName, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using (var output = new FileStream(installer, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                await response.Content.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            await using (var input = File.OpenRead(installer))
            {
                var actual = await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false);
                if (!CryptographicOperations.FixedTimeEquals(expected, actual))
                    throw new InvalidDataException("The downloaded installer checksum does not match the signed update manifest.");
            }

            return installer;
        }
        catch
        {
            File.Delete(installer);
            Directory.Delete(directory);
            throw;
        }
    }

    private async Task<byte[]> ReadSmallAssetAsync(string uri, int limit, CancellationToken token)
    {
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var content = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var buffer = new byte[limit + 1];
        int length = await content.ReadAtLeastAsync(buffer, buffer.Length, false, token).ConfigureAwait(false);
        if (length > limit)
            throw new InvalidDataException("The update manifest or signature is larger than expected.");
        return buffer[..length];
    }

    private byte[] ReadVerifiedDigest(byte[] manifest, byte[] signature, string tag, string installerName)
    {
        using var rsa = RSA.Create();
        byte[] key = Convert.FromBase64String(publicKey);
        rsa.ImportSubjectPublicKeyInfo(key, out int read);
        if (read != key.Length || rsa.KeySize < 2048
            || !rsa.VerifyData(manifest, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new InvalidDataException("The update manifest signature could not be verified.");

        using var document = JsonDocument.Parse(manifest);
        var release = document.RootElement;
        if (release.ValueKind != JsonValueKind.Object
            || !release.TryGetProperty("tag", out var signedTag) || signedTag.ValueKind != JsonValueKind.String || signedTag.GetString() != tag
            || !release.TryGetProperty("sha256", out var files) || files.ValueKind != JsonValueKind.Object
            || !files.TryGetProperty(installerName, out var sha256) || sha256.ValueKind != JsonValueKind.String
            || sha256.GetString() is not { Length: 64 } hex || !hex.All(Uri.IsHexDigit))
            throw new InvalidDataException("The signed manifest does not identify the installer for this version.");
        return Convert.FromHexString(hex);
    }
}
