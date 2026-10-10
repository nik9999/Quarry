using Quarry.Core.Abstractions;
using Quarry.Core.Documents;
using Quarry.Ingestion.Embeddings;
using Quarry.Ingestion.Tests.Fakes;
using Xunit;

namespace Quarry.Ingestion.Tests.Embeddings;

public class BatchEmbeddingPipelineTests
{
    [Fact]
    public async Task EmbedBatchAsync_SplitsIntoCorrectBatches()
    {
        var fakeGen = new FakeEmbeddingGenerator(dimensions: 384);
        var pipeline = new BatchEmbeddingPipeline(fakeGen, embeddingModel: "test", batchSize: 2);

        var chunks = new List<DocumentChunk>
        {
            new("c-1", "d-1", 0, "текст 1"),
            new("c-2", "d-1", 1, "текст 2"),
            new("c-3", "d-1", 2, "текст 3"),
            new("c-4", "d-1", 3, "текст 4"),
            new("c-5", "d-1", 4, "текст 5")
        };

        var results = await pipeline.EmbedBatchAsync(chunks);

        Assert.Equal(5, results.Count);
        Assert.Equal(5, fakeGen.CapturedInputs.Count);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(chunks[i].ChunkId, results[i].ChunkId);
        }
    }

    [Fact]
    public async Task EmbedBatchAsync_EmptyCollection_ReturnsEmptyList()
    {
        var fakeGen = new FakeEmbeddingGenerator(dimensions: 384);
        var pipeline = new BatchEmbeddingPipeline(fakeGen, embeddingModel: "test");

        var results = await pipeline.EmbedBatchAsync(Array.Empty<DocumentChunk>());

        Assert.Empty(results);
        Assert.Empty(fakeGen.CapturedInputs);
    }

    [Fact]
    public async Task EmbedStreamAsync_StreamsBatchesCorrectly()
    {
        var fakeGen = new FakeEmbeddingGenerator(dimensions: 384);
        var pipeline = new BatchEmbeddingPipeline(fakeGen, embeddingModel: "test", batchSize: 2);

        async IAsyncEnumerable<DocumentChunk> StreamAsync()
        {
            yield return new DocumentChunk("c-1", "d-1", 0, "текст 1");
            yield return new DocumentChunk("c-2", "d-1", 1, "текст 2");
            yield return new DocumentChunk("c-3", "d-1", 2, "текст 3");
            await Task.Yield();
        }

        var results = new List<EmbeddedChunk>();
        await foreach (var item in pipeline.EmbedStreamAsync(StreamAsync()))
        {
            results.Add(item);
        }

        Assert.Equal(3, results.Count);
        Assert.Equal("c-1", results[0].ChunkId);
        Assert.Equal("c-2", results[1].ChunkId);
        Assert.Equal("c-3", results[2].ChunkId);
    }
}
