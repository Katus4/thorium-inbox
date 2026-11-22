using System.Net;
using System.Text;
using System.Text.Json;
using ThoriumInbox.Core;

namespace ThoriumInbox.Tests;

public sealed class GitHubReleaseFeedTests
{
    private const string WindowsInstaller = "ThoriumInbox-Setup-x64.exe";
    private const string Notes = "## New features\n\n- New folder options\n\n## Fixes\n\n- Import retry";

    [Fact]
    public async Task ReturnsPublishedVersionWithItsOriginalNotesAndGitHubPage()
    {
        var handler = new ResponseHandler(HttpStatusCode.OK, ReleaseJson("v0.2.0"));
        using var client = new HttpClient(handler);

        var update = await new GitHubReleaseFeed(client)
            .FindUpdateAsync(new Version(0, 1, 0, 0), WindowsInstaller);

        Assert.NotNull(update);
        Assert.Equal(new Version(0, 2, 0), update.Version);
        Assert.Equal(Notes, update.Notes);
        Assert.Equal("https://github.com/Katus4/thorium-inbox/releases/tag/v0.2.0", update.Page.AbsoluteUri);
        Assert.Equal("https://api.github.com/repos/Katus4/thorium-inbox/releases/latest", handler.RequestUri);
    }

    [Theory]
    [InlineData("v0.1.0", false, false, WindowsInstaller)]
    [InlineData("v0.1.1", true, false, WindowsInstaller)]
    [InlineData("v0.1.1", false, true, WindowsInstaller)]
    [InlineData("v0.1.1", false, false, "ThoriumInbox-Setup-x64-Unsigned-Test.exe")]
    [InlineData("v0.1.1/other", false, false, WindowsInstaller)]
    public async Task SkipsCurrentUnpublishedOrIncompatibleReleases(string tag, bool draft, bool prerelease, string asset)
    {
        using var client = new HttpClient(new ResponseHandler(HttpStatusCode.OK, ReleaseJson(tag, draft, prerelease, asset)));
        Assert.Null(await new GitHubReleaseFeed(client).FindUpdateAsync(new Version(0, 1, 0, 0), WindowsInstaller));
    }

    [Fact]
    public async Task EmptyReleaseHistoryHasNoUpdate()
    {
        using var client = new HttpClient(new ResponseHandler(HttpStatusCode.NotFound, "{}"));
        Assert.Null(await new GitHubReleaseFeed(client).FindUpdateAsync(new Version(0, 1, 0), WindowsInstaller));
    }

    private static string ReleaseJson(string tag, bool draft = false, bool prerelease = false, string asset = WindowsInstaller) =>
        JsonSerializer.Serialize(new
        {
            tag_name = tag,
            draft,
            prerelease,
            body = Notes,
            html_url = "https://unrelated.example/",
            assets = new[] { new { name = asset, state = "uploaded" } },
        });

    private sealed class ResponseHandler(HttpStatusCode status, string json) : HttpMessageHandler
    {
        public string? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
