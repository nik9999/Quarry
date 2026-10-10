namespace Quarry.Ingestion.Chunking;

using Quarry.Core.Documents;

/// <summary>
/// Вспомогательный построитель чанков с унифицированным формированием идентификаторов и метаданных.
/// </summary>
internal static class ChunkBuilder
{
    public static DocumentChunk Create(
        Document document,
        int index,
        string text,
        int startOffset,
        int endOffset,
        IReadOnlyDictionary<string, string>? additionalMetadata = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(text);

        var chunkId = $"{document.Id}#{index}";

        Dictionary<string, string>? metadata = null;
        if (document.Metadata is not null && document.Metadata.Count > 0)
        {
            metadata = new Dictionary<string, string>(document.Metadata);
        }

        if (additionalMetadata is not null && additionalMetadata.Count > 0)
        {
            metadata ??= new Dictionary<string, string>();
            foreach (var (k, v) in additionalMetadata)
            {
                metadata[k] = v;
            }
        }

        return new DocumentChunk(
            chunkId,
            document.Id,
            index,
            text,
            metadata,
            startOffset,
            endOffset);
    }
}
