namespace Quarry.Ingestion.Multimodal;

using Quarry.Core.Documents;

/// <summary>
/// Мультимодальный фрагмент документа, содержащий текстовое описание и возможные бинарные медиаданные (изображения).
/// </summary>
public sealed record MultimodalChunk(
    string ChunkId,
    string DocumentId,
    int Index,
    string Text,
    int PageNumber,
    ReadOnlyMemory<byte>? ImageData = null,
    string? ImageMimeType = null,
    string? ImageCaption = null,
    IReadOnlyDictionary<string, string>? Metadata = null,
    int StartCharOffset = 0,
    int EndCharOffset = 0)
{
    /// <summary>
    /// Преобразует мультимодальный фрагмент в стандартный <see cref="DocumentChunk"/> для векторизации и сохранения.
    /// </summary>
    public DocumentChunk ToDocumentChunk()
    {
        var meta = new Dictionary<string, string>();
        if (Metadata is not null)
        {
            foreach (var (k, v) in Metadata)
            {
                meta[k] = v;
            }
        }

        meta["page_number"] = PageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (!string.IsNullOrEmpty(ImageCaption))
        {
            meta["has_image"] = "true";
            meta["image_caption"] = ImageCaption;
        }

        var fullText = !string.IsNullOrEmpty(ImageCaption)
            ? $"{Text}\n\n[Изображение: {ImageCaption}]".Trim()
            : Text;

        return new DocumentChunk(
            ChunkId,
            DocumentId,
            Index,
            fullText,
            meta,
            StartCharOffset,
            EndCharOffset);
    }
}
