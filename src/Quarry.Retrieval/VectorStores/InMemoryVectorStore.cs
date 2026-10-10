namespace Quarry.Retrieval.VectorStores;

using Quarry.Core.Abstractions;
using Quarry.Core.Documents;
using Quarry.Core.Filtering;
using Quarry.Core.Numerics;

/// <summary>
/// Быстрое потокобезопасное хранилище векторных эмбеддингов в оперативной памяти.
/// Использует SIMD-ускорение <see cref="CosineKernel"/> и линейный поиск. Идеально для тестов и локальной отладки.
/// </summary>
public sealed class InMemoryVectorStore : IVectorStore
{
    private readonly List<EmbeddedChunk> _chunks = [];
    private readonly Lock _lock = new();

    public string CollectionName { get; }

    public InMemoryVectorStore(string collectionName = "in-memory")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);
        CollectionName = collectionName;
    }

    public Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task UpsertAsync(IEnumerable<EmbeddedChunk> chunks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        lock (_lock)
        {
            foreach (var c in chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var index = _chunks.FindIndex(existing => existing.Chunk.ChunkId == c.Chunk.ChunkId);
                if (index >= 0)
                {
                    _chunks[index] = c;
                }
                else
                {
                    _chunks.Add(c);
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RetrievalResult>> SearchAsync(
        ReadOnlyMemory<float> queryVector,
        int topK,
        MetadataFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        if (topK <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(topK), "topK must be greater than zero.");
        }

        EmbeddedChunk[] snapshot;
        lock (_lock)
        {
            snapshot = _chunks.ToArray();
        }

        var results = snapshot
            .Where(c => filter is null || filter.Matches(c.Chunk.Metadata))
            .Select(c => new RetrievalResult(c.Chunk, CosineKernel.CosineSimilarity(queryVector.Span, c.Vector.Span)))
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .ToList();

        return Task.FromResult<IReadOnlyList<RetrievalResult>>(results);
    }

    public Task DeleteAsync(IEnumerable<string> chunkIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunkIds);
        var ids = chunkIds.ToHashSet(StringComparer.Ordinal);

        lock (_lock)
        {
            _chunks.RemoveAll(c => ids.Contains(c.Chunk.ChunkId));
        }

        return Task.CompletedTask;
    }
}
