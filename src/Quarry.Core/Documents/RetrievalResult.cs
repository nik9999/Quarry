namespace Quarry.Core.Documents;

/// <summary>
/// Результат векторного или гибридного поиска: найденный фрагмент документа и оценка его релевантности.
/// </summary>
public sealed record RetrievalResult(
    DocumentChunk Chunk,
    double Score)
{
    public string ChunkId => Chunk.ChunkId;
    public string DocumentId => Chunk.DocumentId;
    public string Text => Chunk.Text;
}
