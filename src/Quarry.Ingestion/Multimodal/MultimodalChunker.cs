namespace Quarry.Ingestion.Multimodal;

using System.Runtime.CompilerServices;
using Quarry.Core.Abstractions;
using Quarry.Core.Documents;

/// <summary>
/// Мультимодальный обработчик документов (PDF): извлекает текст страниц, внедренные изображения,
/// генерирует кэпшены и формирует мультимодальные фрагменты.
/// </summary>
public sealed class MultimodalChunker
{
    private readonly IPdfImageExtractor _pdfExtractor;
    private readonly IImageCaptioner _captioner;
    private readonly IChunker? _textChunker;

    public MultimodalChunker(
        IPdfImageExtractor? pdfExtractor = null,
        IImageCaptioner? captioner = null,
        IChunker? textChunker = null)
    {
        _pdfExtractor = pdfExtractor ?? new PdfPigImageExtractor();
        _captioner = captioner ?? new DefaultImageCaptioner();
        _textChunker = textChunker;
    }

    /// <summary>
    /// Извлекает мультимодальные фрагменты из PDF потока.
    /// </summary>
    public async IAsyncEnumerable<MultimodalChunk> ChunkPdfAsync(
        string documentId,
        Stream pdfStream,
        IReadOnlyDictionary<string, string>? documentMetadata = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        ArgumentNullException.ThrowIfNull(pdfStream);

        var pages = _pdfExtractor.Extract(pdfStream);
        var globalIndex = 0;

        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pageMeta = new Dictionary<string, string>();
            if (documentMetadata is not null)
            {
                foreach (var (k, v) in documentMetadata)
                {
                    pageMeta[k] = v;
                }
            }

            pageMeta["page_number"] = page.PageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);

            // Caption each image on this page
            var imageCaptions = new List<(PdfExtractedImage Image, string Caption)>();
            foreach (var img in page.Images)
            {
                var caption = await _captioner.CaptionAsync(img.Bytes, img.MimeType, cancellationToken).ConfigureAwait(false);
                imageCaptions.Add((img, caption));
            }

            if (_textChunker is not null && !string.IsNullOrWhiteSpace(page.Text))
            {
                var pageDoc = new Document($"{documentId}#p{page.PageNumber}", page.Text, pageMeta);
                await foreach (var textChunk in _textChunker.ChunkAsync(pageDoc, cancellationToken).ConfigureAwait(false))
                {
                    yield return new MultimodalChunk(
                        $"{documentId}#mm_{globalIndex++}",
                        documentId,
                        globalIndex - 1,
                        textChunk.Text,
                        page.PageNumber,
                        Metadata: textChunk.Metadata,
                        StartCharOffset: textChunk.StartCharOffset,
                        EndCharOffset: textChunk.EndCharOffset);
                }
            }
            else
            {
                yield return new MultimodalChunk(
                    $"{documentId}#mm_{globalIndex++}",
                    documentId,
                    globalIndex - 1,
                    page.Text,
                    page.PageNumber,
                    Metadata: pageMeta,
                    StartCharOffset: 0,
                    EndCharOffset: page.Text.Length);
            }

            // Emit image chunks if present
            foreach (var (img, caption) in imageCaptions)
            {
                var imgMeta = new Dictionary<string, string>(pageMeta)
                {
                    ["image_name"] = img.Name,
                    ["image_mime"] = img.MimeType,
                    ["image_width"] = img.Width.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["image_height"] = img.Height.ToString(System.Globalization.CultureInfo.InvariantCulture)
                };

                yield return new MultimodalChunk(
                    $"{documentId}#img_{globalIndex++}",
                    documentId,
                    globalIndex - 1,
                    $"[Изображение: {caption}]",
                    page.PageNumber,
                    ImageData: img.Bytes,
                    ImageMimeType: img.MimeType,
                    ImageCaption: caption,
                    Metadata: imgMeta,
                    StartCharOffset: 0,
                    EndCharOffset: 0);
            }
        }
    }
}
