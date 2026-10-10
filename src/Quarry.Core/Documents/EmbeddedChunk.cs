namespace Quarry.Core.Documents;

/// <summary>
/// Представляет чанк документа, связанный с его векторным эмбеддингом.
/// </summary>
public sealed record EmbeddedChunk(
    DocumentChunk Chunk,
    ReadOnlyMemory<float> Vector,
    string EmbeddingModel)
{
    public string ChunkId => Chunk.ChunkId;
    public string DocumentId => Chunk.DocumentId;
    public string Text => Chunk.Text;
}
