using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ThoriumInbox.Core;

namespace ThoriumInbox.Tests;

public sealed class InboxTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("thorium-inbox-tests-").FullName;
    private readonly TestClock clock = new();
    private readonly List<string> imports = [];
    private readonly List<InboxActivity> activity = [];

    private string Inbox => Path.Combine(root, "Inbox");
    private string Output => Path.Combine(root, "EPUBs");
    private string State => Path.Combine(root, "imports.json");

    [Fact]
    public async Task LargeFoldoutPreservesResolutionOfOrdinaryPages()
    {
        var processor = CreateProcessor();
        await File.WriteAllBytesAsync(Path.Combine(Inbox, "foldout.pdf"), SamplePdf(oversized: true));
        await SettleAsync(processor);

        using var archive = ZipFile.OpenRead(Assert.Single(imports));
        Assert.Contains("width=375", ReadEntry(archive, "EPUB/page-1.xhtml"));
        var extent = XElement.Parse(ReadEntry(archive, "EPUB/page-2.xhtml"))
            .Descendants(XName.Get("img", "http://www.w3.org/1999/xhtml")).Single();
        Assert.InRange((int)extent.Attribute("width")!, 2000, 2400);
    }

    [Fact]
    public async Task CompletedPdfBecomesAnOrderedFixedLayoutEpubAndOriginalStaysInInbox()
    {
        var processor = CreateProcessor();
        var pdf = Path.Combine(Inbox, "Science & maps.PDF");
        await File.WriteAllBytesAsync(pdf, SamplePdf());

        Assert.True(await processor.ScanAsync());
        Assert.Empty(imports);
        await SettleAsync(processor);

        string epub = Assert.Single(imports);
        Assert.Equal(Output, Path.GetDirectoryName(epub));
        Assert.True(File.Exists(pdf));
        using var archive = ZipFile.OpenRead(epub);
        Assert.Equal("mimetype", archive.Entries[0].FullName);
        Assert.Equal("application/epub+zip", ReadEntry(archive, "mimetype"));
        Assert.NotNull(archive.GetEntry("META-INF/container.xml"));
        XNamespace opf = "http://www.idpf.org/2007/opf";
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        var package = XElement.Parse(ReadEntry(archive, "EPUB/package.opf"));
        Assert.Equal("Science & maps", package.Descendants(dc + "title").Single().Value);
        Assert.Contains(package.Descendants(opf + "meta"), element =>
            (string?)element.Attribute("property") == "rendition:layout" && element.Value == "pre-paginated");
        Assert.Equal(new[] { "page-1", "page-2" },
            package.Descendants(opf + "itemref").Select(e => (string?)e.Attribute("idref")));
        Assert.True(archive.GetEntry("EPUB/page-2.jpg")!.Length > 1_000);
        Assert.Contains("page-2.xhtml", ReadEntry(archive, "EPUB/nav.xhtml"));
        Assert.Empty(Directory.GetFiles(Output, "*.tmp"));
    }

    [Fact]
    public async Task OnlyImportedContentIsDeduplicatedAcrossRestartsAndRenames()
    {
        var processor = CreateProcessor();
        var source = Path.Combine(Inbox, "article.epub");
        await File.WriteAllTextAsync(source, "test input sent directly to the reader");
        await SettleAsync(processor);
        Assert.Equal(source, Assert.Single(imports));

        File.Copy(source, Path.Combine(Inbox, "article-copy.epub"));
        processor = CreateProcessor();
        await SettleAsync(processor);
        Assert.Single(imports);
        Assert.True(File.Exists(State));
    }

    [Fact]
    public async Task IncompleteAndLockedDownloadsWaitForACompletedFile()
    {
        var processor = CreateProcessor();
        string partial = Path.Combine(Inbox, "download.pdf.crdownload");
        await File.WriteAllBytesAsync(partial, SamplePdf());
        Assert.False(await processor.ScanAsync());
        Assert.Empty(imports);

        string finished = Path.Combine(Inbox, "download.pdf");
        File.Move(partial, finished);
        using (var writer = File.Open(finished, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
        {
            await SettleAsync(processor);
            Assert.Empty(imports);
            Assert.DoesNotContain(activity, item => item.IsError);
        }

        await processor.ScanAsync();
        Assert.Single(imports);
    }

    [Fact]
    public async Task FailedImportLeavesTheOriginalAndRetriesTheSameEpub()
    {
        var attempts = 0;
        var processor = CreateProcessor((path, _) =>
        {
            imports.Add(path);
            return ++attempts == 1 ? Task.FromException(new IOException("Reader unavailable")) : Task.CompletedTask;
        });
        string source = Path.Combine(Inbox, "newsletter.pdf");
        await File.WriteAllBytesAsync(source, SamplePdf());
        await SettleAsync(processor);
        Assert.Single(imports);
        Assert.False(File.Exists(State));
        Assert.Contains(activity, item => item.IsError && item.Message.Contains("Reader unavailable"));

        clock.Advance(TimeSpan.FromMinutes(2));
        await processor.ScanAsync();
        Assert.Equal(2, imports.Count);
        Assert.Equal(imports[0], imports[1]);
        Assert.True(File.Exists(State));
        Assert.True(File.Exists(source));
        Assert.Single(Directory.GetFiles(Output));
    }

    [Fact]
    public async Task DirectEpubIsReadableWhileTheImporterRuns()
    {
        string? read = null;
        var processor = CreateProcessor(async (path, token) =>
        {
            read = await File.ReadAllTextAsync(path, token);
        });
        await File.WriteAllTextAsync(Path.Combine(Inbox, "article.epub"), "Existing EPUB content");

        await SettleAsync(processor);

        Assert.Equal("Existing EPUB content", read);
        Assert.DoesNotContain(activity, item => item.IsError);
    }

    [LinuxFact]
    public async Task CaseDistinctLinuxPathsBothSettleAndImport()
    {
        var processor = CreateProcessor();
        string first = Path.Combine(Inbox, "Article.epub");
        string second = Path.Combine(Inbox, "article.epub");
        await File.WriteAllTextAsync(first, "First content");
        await File.WriteAllTextAsync(second, "Separate content and length");

        await SettleAsync(processor);

        Assert.Equal(2, imports.Count);
        Assert.Contains(first, imports);
        Assert.Contains(second, imports);
    }

    [LinuxFact]
    public async Task UnicodePdfNameFitsInTheOutputFileSystemComponent()
    {
        var processor = CreateProcessor();
        string input = Path.Combine(Inbox, new string('本', 82) + ".pdf");
        await File.WriteAllBytesAsync(input, SamplePdf());

        await SettleAsync(processor);

        Assert.True(File.Exists(Assert.Single(imports)));
        Assert.True(File.Exists(input));
    }

    [Fact]
    public async Task BrokenPdfIsReportedAndDoesNotPublishAPartialEpub()
    {
        var processor = CreateProcessor();
        var source = Path.Combine(Inbox, "broken.pdf");
        await File.WriteAllTextAsync(source, "%PDF-1.4\nIncomplete");
        await SettleAsync(processor);

        Assert.Empty(imports);
        Assert.Empty(Directory.GetFiles(Output));
        Assert.True(File.Exists(source));
        Assert.Contains(activity, item => item.IsError && item.Message.Contains("broken.pdf"));
    }

    private InboxProcessor CreateProcessor(Func<string, CancellationToken, Task>? importer = null)
    {
        Directory.CreateDirectory(Inbox);
        Directory.CreateDirectory(Output);
        return new InboxProcessor(Inbox, Output, State, importer ?? ((path, _) =>
        {
            imports.Add(path);
            return Task.CompletedTask;
        }), activity.Add, clock);
    }

    private async Task SettleAsync(InboxProcessor processor)
    {
        await processor.ScanAsync();
        clock.Advance(TimeSpan.FromSeconds(3));
        await processor.ScanAsync();
    }

    private static string ReadEntry(ZipArchive archive, string name)
    {
        using var reader = new StreamReader(archive.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }

    internal static byte[] SamplePdf(bool oversized = false)
    {
        // Known page count, geometry, and artwork, generated without a library under test.
        string firstPage = "0.15 0.32 0.43 rg 22 30 120 240 re f\n";
        string secondPage = "0.74 0.29 0.12 rg 20 22 240 140 re f\n";
        string[] objects = [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Count 2 /Kids [3 0 R 4 0 R] >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 180 300] /Contents 5 0 R >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {(oversized ? "14400" : "300")} 180] /Contents 6 0 R >>",
            $"<< /Length {firstPage.Length} >>\nstream\n{firstPage}endstream",
            $"<< /Length {secondPage.Length} >>\nstream\n{secondPage}endstream",
        ];
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }
        var xref = pdf.Length;
        pdf.Append($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            pdf.Append($"{offset:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private sealed class LinuxFactAttribute : FactAttribute
    {
        public LinuxFactAttribute()
        {
            if (!OperatingSystem.IsLinux())
                Skip = "This test exercises the Linux file-system behavior.";
        }
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 21, 6, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan interval) => now += interval;
    }
}
