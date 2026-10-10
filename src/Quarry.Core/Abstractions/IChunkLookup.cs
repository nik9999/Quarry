using Quarry.Core.Documents;

namespace Quarry.Core.Abstractions;

/// <summary>
/// By-id lookup port for retrieving a single <see cref="DocumentChunk"/> from
/// the document store. Separate from <see cref="IRetriever"/> (which ranks by
/// relevance) because tools and resources need exact identity resolution, not
/// similarity search. Implementations resolve against whatever backs the
/// corpus — an in-memory dictionary in samples, the vector/document store
/// in production.
/// </summary>
public interface IChunkLookup
{
    /// <summary>
    /// Resolve the chunk whose <see cref="DocumentChunk.ChunkId"/> equals
    /// <paramref name="chunkId"/>, or <see langword="null"/> when no such chunk
    /// exists. Identity only — the caller is responsible for any authorization
    /// (clearance / tenant) check on the returned chunk's metadata.
    /// </summary>
    Task<DocumentChunk?> GetByIdAsync(string chunkId, CancellationToken cancellationToken = default);
}
