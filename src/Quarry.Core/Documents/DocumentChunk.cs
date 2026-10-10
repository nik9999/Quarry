namespace Quarry.Core.Documents;

/// <summary>
/// Представляет отдельный фрагмент (чанк) документа с поддержкой смещений символов для точного цитирования первоисточника.
/// </summary>
public sealed record DocumentChunk(
    string ChunkId,
    string DocumentId,
    int Index,
    string Text,
    IReadOnlyDictionary<string, string>? Metadata = null,
    int StartCharOffset = 0,
    int EndCharOffset = 0)
{
    private DocumentMetadata? _docMetadata;

    /// <summary>Zero-based index of this chunk within the parent document.</summary>
    public int ChunkIndex => Index;

    /// <summary>
    /// Optional provenance signature attached at ingest; null until signed.
    /// </summary>
    public string? Provenance { get; init; }

    /// <summary>
    /// Strongly-typed document metadata, if available or convertible from dictionary.
    /// </summary>
    public DocumentMetadata? DocMetadata
    {
        get => _docMetadata ??= DocumentMetadata.FromDictionary(Metadata);
        init => _docMetadata = value;
    }

    /// <summary>
    /// Конструктор для позиционной инициализации со смещениями.
    /// </summary>
    public DocumentChunk(
        string chunkId,
        string documentId,
        int index,
        string text,
        int startCharOffset,
        int endCharOffset,
        IReadOnlyDictionary<string, string>? metadata = null)
        : this(chunkId, documentId, index, text, metadata, startCharOffset, endCharOffset)
    {
    }

    /// <summary>
    /// Конструктор для типизированных метаданных DocumentMetadata.
    /// </summary>
    public DocumentChunk(
        string chunkId,
        string documentId,
        int index,
        string text,
        int startCharOffset,
        int endCharOffset,
        DocumentMetadata metadata)
        : this(chunkId, documentId, index, text, metadata?.ToDictionary(), startCharOffset, endCharOffset)
    {
        DocMetadata = metadata;
    }
}
