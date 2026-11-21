using System.Globalization;
using System.Drawing;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using PDFtoImage;
using SkiaSharp;

namespace ThoriumInbox.Core;

internal static class PdfEpubConverter
{
    private static readonly XNamespace Html = "http://www.w3.org/1999/xhtml";
    private static readonly XNamespace Epub = "http://www.idpf.org/2007/ops";
    private static readonly XNamespace Opf = "http://www.idpf.org/2007/opf";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Container = "urn:oasis:names:tc:opendocument:xmlns:container";
    private static readonly Encoding Utf8 = new UTF8Encoding(false);

    public static void Convert(Stream pdf, string title, string sourceHash, Stream destination,
        CancellationToken cancellationToken)
    {
        var sizes = Conversion.GetPageSizes(pdf, leaveOpen: true).ToArray();
        if (sizes.Length == 0)
            throw new InvalidDataException("The PDF has no pages.");

        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        WriteText(archive, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
        WriteXml(archive, "META-INF/container.xml", new XElement(Container + "container",
            new XAttribute("version", "1.0"),
            new XElement(Container + "rootfiles", new XElement(Container + "rootfile",
                new XAttribute("full-path", "EPUB/package.opf"),
                new XAttribute("media-type", "application/oebps-package+xml")))));

        var manifest = new XElement(Opf + "manifest", ManifestItem("nav", "nav.xhtml", "application/xhtml+xml", "nav"));
        var spine = new XElement(Opf + "spine");
        var toc = new XElement(Html + "ol");
        int pageNumber = 0;
        foreach (var bitmap in RenderPages(pdf, sizes, cancellationToken))
        {
            using (bitmap)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string number = (++pageNumber).ToString(CultureInfo.InvariantCulture);
                string pageId = $"page-{number}";
                string pageFile = $"{pageId}.xhtml";
                string imageFile = $"{pageId}.jpg";
                string pageTitle = $"Page {number}";

                using (var imageData = bitmap.Encode(SKEncodedImageFormat.Jpeg, 90)
                       ?? throw new InvalidDataException($"Could not render page {number}."))
                using (Stream imageEntry = archive.CreateEntry($"EPUB/{imageFile}", CompressionLevel.NoCompression).Open())
                    imageData.SaveTo(imageEntry);

                WriteXml(archive, $"EPUB/{pageFile}", HtmlDocument(pageTitle,
                    new XElement(Html + "img", new XAttribute("src", imageFile), new XAttribute("alt", pageTitle),
                        new XAttribute("width", bitmap.Width), new XAttribute("height", bitmap.Height)),
                    new XElement(Html + "meta", new XAttribute("name", "viewport"),
                        new XAttribute("content", FormattableString.Invariant($"width={bitmap.Width}, height={bitmap.Height}"))),
                    new XElement(Html + "style", "html, body { margin: 0; padding: 0; } img { display: block; }")));

                manifest.Add(ManifestItem(pageId, pageFile, "application/xhtml+xml"));
                manifest.Add(ManifestItem($"image-{number}", imageFile, "image/jpeg", pageNumber == 1 ? "cover-image" : null));
                spine.Add(new XElement(Opf + "itemref", new XAttribute("idref", pageId)));
                toc.Add(new XElement(Html + "li", new XElement(Html + "a", new XAttribute("href", pageFile), pageTitle)));
            }
        }

        string bookId = new Guid(System.Convert.FromHexString(sourceHash[..32])).ToString();
        WriteXml(archive, "EPUB/package.opf", new XElement(Opf + "package",
            new XAttribute("version", "3.0"), new XAttribute("unique-identifier", "publication-id"),
            new XElement(Opf + "metadata", new XAttribute(XNamespace.Xmlns + "dc", Dc),
                new XElement(Dc + "identifier", new XAttribute("id", "publication-id"), $"urn:uuid:{bookId}"),
                new XElement(Dc + "title", title), new XElement(Dc + "language", "und"),
                Metadata("dcterms:modified", DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)),
                Metadata("rendition:layout", "pre-paginated"), Metadata("rendition:spread", "none")),
            manifest, spine));

        WriteXml(archive, "EPUB/nav.xhtml", HtmlDocument(title,
            new XElement(Html + "nav", new XAttribute(XNamespace.Xmlns + "epub", Epub),
                new XAttribute(Epub + "type", "toc"), new XElement(Html + "h1", "Pages"), toc)));
    }

    private static IEnumerable<SKBitmap> RenderPages(Stream pdf, SizeF[] sizes, CancellationToken token)
    {
        const int standardDpi = 150;
        var resolutions = new int[sizes.Length];
        for (int index = 0; index < sizes.Length; index++)
        {
            float edge = Math.Max(sizes[index].Width, sizes[index].Height);
            if (!float.IsFinite(edge) || edge <= 0 || edge > 172_800)
                throw new InvalidDataException("The PDF has an unsupported page size.");
            resolutions[index] = Math.Min(standardDpi, (int)Math.Floor(2400 * 72 / edge));
        }

        // Keep ordinary PDFs on one open PDFium document. Large foldouts need a different DPI per page.
        pdf.Position = 0;
        if (resolutions.All(dpi => dpi == resolutions[0]))
        {
            foreach (var image in Conversion.ToImages(pdf, leaveOpen: true,
                         options: new RenderOptions(Dpi: resolutions[0], BackgroundColor: SKColors.White)))
                yield return image;
            yield break;
        }

        for (int index = 0; index < sizes.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            pdf.Position = 0;
            yield return Conversion.ToImage(pdf, index, leaveOpen: true,
                options: new RenderOptions(Dpi: resolutions[index], BackgroundColor: SKColors.White));
        }
    }

    private static XElement HtmlDocument(string title, XElement body, params XElement[] headItems) =>
        new(Html + "html", new XAttribute("lang", "en"), new XAttribute(XNamespace.Xml + "lang", "en"),
            new XElement(Html + "head", new XElement(Html + "title", title), headItems),
            new XElement(Html + "body", body));

    private static XElement ManifestItem(string id, string path, string mediaType, string? properties = null) =>
        new(Opf + "item", new XAttribute("id", id), new XAttribute("href", path),
            new XAttribute("media-type", mediaType), properties is null ? null : new XAttribute("properties", properties));

    private static XElement Metadata(string property, string value) =>
        new(Opf + "meta", new XAttribute("property", property), value);

    private static void WriteXml(ZipArchive archive, string path, XElement document) =>
        WriteText(archive, path, document.ToString(SaveOptions.DisableFormatting));

    private static void WriteText(ZipArchive archive, string path, string text,
        CompressionLevel level = CompressionLevel.Optimal)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path, level).Open(), Utf8);
        writer.Write(text);
    }
}
