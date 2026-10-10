namespace Quarry.Ingestion.Multimodal;

using UglyToad.PdfPig;

/// <summary>
/// Реализация извлечения страниц и изображений из PDF на базе библиотеки PdfPig.
/// </summary>
public sealed class PdfPigImageExtractor : IPdfImageExtractor
{
    public IReadOnlyList<PdfExtractedPage> Extract(Stream pdfStream)
    {
        ArgumentNullException.ThrowIfNull(pdfStream);

        using var document = PdfDocument.Open(pdfStream);
        return ExtractInternal(document);
    }

    public IReadOnlyList<PdfExtractedPage> Extract(byte[] pdfBytes)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);

        using var document = PdfDocument.Open(pdfBytes);
        return ExtractInternal(document);
    }

    private static List<PdfExtractedPage> ExtractInternal(PdfDocument document)
    {
        var pages = new List<PdfExtractedPage>();

        foreach (var page in document.GetPages())
        {
            var images = new List<PdfExtractedImage>();

            var imgIndex = 0;
            foreach (var img in page.GetImages())
            {
                byte[] bytes;
                string mimeType;

                if (img.TryGetPng(out var pngBytes))
                {
                    bytes = pngBytes;
                    mimeType = "image/png";
                }
                else
                {
                    bytes = img.RawBytes.ToArray();
                    mimeType = "application/octet-stream";
                }

                var width = (int)Math.Round(img.Bounds.Width);
                var height = (int)Math.Round(img.Bounds.Height);
                var name = $"page_{page.Number}_img_{imgIndex++}";

                images.Add(new PdfExtractedImage(bytes, mimeType, width, height, name));
            }

            pages.Add(new PdfExtractedPage(page.Number, page.Text, images));
        }

        return pages;
    }
}
