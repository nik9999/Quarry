namespace Quarry.Ingestion.Tests.Fakes;

using Quarry.Core.Abstractions;
using Quarry.Core.Documents;
using Quarry.Core.Filtering;

public sealed class FakeVectorStore : IVectorStore
{
    public string CollectionName => "test_collection";
    public bool CollectionExistsCalled { get; private set; }
    public List<EmbeddedChunk> UpsertedChunks { get; } = new();
    public List<string> DeletedChunkIds { get; } = new();

    public Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        CollectionExistsCalled = true;
        return Task.CompletedTask;
    }

    public Task UpsertAsync(IEnumerable<EmbeddedChunk> chunks, CancellationToken cancellationToken = default)
    {
        UpsertedChunks.AddRange(chunks);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RetrievalResult>> SearchAsync(
        ReadOnlyMemory<float> queryVector,
        int topK,
        MetadataFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        var results = UpsertedChunks
            .Take(topK)
            .Select(c => new RetrievalResult(c.Chunk, 0.9))
            .ToList();

        return Task.FromResult<IReadOnlyList<RetrievalResult>>(results);
    }

    public Task DeleteAsync(IEnumerable<string> chunkIds, CancellationToken cancellationToken = default)
    {
        DeletedChunkIds.AddRange(chunkIds);
        return Task.CompletedTask;
    }
}
