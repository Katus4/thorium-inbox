using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ThoriumInbox.Core;

namespace ThoriumInbox.Tests;

public sealed class ReleaseInstallerTests
{
    private const string Installer = "ThoriumInbox-linux-x64.deb";
    private static readonly Version TargetVersion = new(1, 2, 3);

    [Fact]
    public async Task DownloadsOnlyTheAuthenticatedInstallerForTheSelectedVersion()
    {
        var root = Directory.CreateTempSubdirectory("thorium-update-");
        try
        {
            using var key = RSA.Create(2048);
            byte[] payload = [11, 22, 33];
            byte[] manifest = Manifest("v1.2.3", payload);
            using var handler = new AssetsHandler(manifest, key.SignData(manifest, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), payload);
            using var http = new HttpClient(handler);
            var installer = new GitHubReleaseInstaller(http, Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));

            string path = await installer.DownloadAsync(TargetVersion, Installer, root.FullName);

            Assert.Equal(Installer, Path.GetFileName(path));
            Assert.Equal(payload, await File.ReadAllBytesAsync(path));
            Assert.All(handler.Requests, request =>
                Assert.StartsWith("https://github.com/Katus4/thorium-inbox/releases/download/v1.2.3/", request.AbsoluteUri));
            Assert.Equal(3, handler.Requests.Count);
        }
        finally { root.Delete(recursive: true); }
    }

    [Theory]
    [InlineData("bad-signature", 2)]
    [InlineData("old-version", 2)]
    [InlineData("tampered-payload", 3)]
    public async Task RejectsInvalidMetadataOrPayloadAndLeavesNoInstaller(string change, int expectedRequests)
    {
        var root = Directory.CreateTempSubdirectory("thorium-update-");
        try
        {
            using var key = RSA.Create(2048);
            byte[] payload = [11, 22, 33];
            byte[] manifest = Manifest(change == "old-version" ? "v1.2.2" : "v1.2.3", payload);
            byte[] signature = key.SignData(manifest, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            if (change == "bad-signature")
                signature[0] ^= 1;
            if (change == "tampered-payload")
                payload[0] ^= 1;
            using var handler = new AssetsHandler(manifest, signature, payload);
            using var http = new HttpClient(handler);
            var installer = new GitHubReleaseInstaller(http, Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));

            await Assert.ThrowsAsync<InvalidDataException>(() => installer.DownloadAsync(TargetVersion, Installer, root.FullName));
            Assert.Empty(Directory.EnumerateFileSystemEntries(root.FullName));
            Assert.Equal(expectedRequests, handler.Requests.Count);
        }
        finally { root.Delete(recursive: true); }
    }

    private static byte[] Manifest(string tag, byte[] payload) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            tag,
            sha256 = new Dictionary<string, string> { [Installer] = Convert.ToHexStringLower(SHA256.HashData(payload)) },
        });

    private sealed class AssetsHandler(byte[] manifest, byte[] signature, byte[] payload) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri!;
            Requests.Add(uri);
            byte[] asset = uri.Segments[^1] switch
            {
                "update-manifest.json" => manifest,
                "update-manifest.sig" => signature,
                Installer => payload,
                _ => throw new InvalidOperationException("Unexpected URL."),
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(asset) });
        }
    }
}
