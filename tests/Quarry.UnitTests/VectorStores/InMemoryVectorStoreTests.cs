namespace Quarry.Retrieval.Tests.VectorStores;

using Quarry.Core.Documents;
using Quarry.Core.Filtering;
using Quarry.Retrieval.VectorStores;
using Xunit;

public class InMemoryVectorStoreTests
{
    private static readonly float[] Vector3DA = [1.0f, 0.0f, 0.0f];
    private static readonly float[] Vector3DB = [0.0f, 1.0f, 0.0f];
    private static readonly float[] Vector3DC = [0.707f, 0.707f, 0.0f];
    private static readonly float[] Vector2D = [1.0f, 0.0f];

    [Fact]
    public async Task SearchAsync_FindsNearestVectorsWithCosineSimilarity()
    {
        var store = new InMemoryVectorStore("test-mem");

        var c1 = new DocumentChunk("c-1", "doc-1", 0, "Chunk 1");
        var c2 = new DocumentChunk("c-2", "doc-1", 1, "Chunk 2");
        var c3 = new DocumentChunk("c-3", "doc-1", 2, "Chunk 3");

        await store.UpsertAsync([
            new EmbeddedChunk(c1, Vector3DA, "model"),
            new EmbeddedChunk(c2, Vector3DB, "model"),
            new EmbeddedChunk(c3, Vector3DC, "model")
        ]);

        var results = await store.SearchAsync(Vector3DA, 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("c-1", results[0].Chunk.ChunkId);
        Assert.Equal(1.0, results[0].Score, precision: 3);

        Assert.Equal("c-3", results[1].Chunk.ChunkId);
        Assert.Equal(0.707, results[1].Score, precision: 3);
    }

    [Fact]
    public async Task SearchAsync_PreFilter_FiltersOutNonMatchingMetadata()
    {
        var store = new InMemoryVectorStore();

        var metaA = new Dictionary<string, string> { ["category"] = "finance" };
        var metaB = new Dictionary<string, string> { ["category"] = "tech" };

        var c1 = new DocumentChunk("c-1", "doc-1", 0, "Finance doc", metaA);
        var c2 = new DocumentChunk("c-2", "doc-1", 1, "Tech doc", metaB);

        await store.UpsertAsync([
            new EmbeddedChunk(c1, Vector2D, "model"),
            new EmbeddedChunk(c2, Vector2D, "model")
        ]);

        var filter = new MetadataFilter("category", "tech");
        var results = await store.SearchAsync(Vector2D, 5, filter);

        Assert.Single(results);
        Assert.Equal("c-2", results[0].Chunk.ChunkId);
    }

    [Fact]
    public async Task DeleteAsync_RemovesChunks()
    {
        var store = new InMemoryVectorStore();
        var c = new DocumentChunk("c-del", "doc-1", 0, "To be deleted");

        await store.UpsertAsync([new EmbeddedChunk(c, Vector2D, "model")]);
        await store.DeleteAsync(["c-del"]);

        var results = await store.SearchAsync(Vector2D, 5);
        Assert.Empty(results);
    }
}
