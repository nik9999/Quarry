namespace Quarry.Ingestion.Multimodal;

/// <summary>
/// Извлеченное изображение из страницы PDF документа.
/// </summary>
public sealed record PdfExtractedImage(
    ReadOnlyMemory<byte> Bytes,
    string MimeType,
    int Width,
    int Height,
    string Name);

/// <summary>
/// Содержимое отдельной страницы PDF документа (текст и внедренные изображения).
/// </summary>
public sealed record PdfExtractedPage(
    int PageNumber,
    string Text,
    IReadOnlyList<PdfExtractedImage> Images);

/// <summary>
/// Интерфейс извлечения текста и графических данных из PDF документов.
/// </summary>
public interface IPdfImageExtractor
{
    /// <summary>
    /// Извлекает страницы с текстом и изображениями из потока PDF.
    /// </summary>
    IReadOnlyList<PdfExtractedPage> Extract(Stream pdfStream);

    /// <summary>
    /// Извлекает страницы с текстом и изображениями из массива байтов PDF.
    /// </summary>
    IReadOnlyList<PdfExtractedPage> Extract(byte[] pdfBytes);
}
