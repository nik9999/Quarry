using Quarry.Core.Abstractions;
using Quarry.Core.Documents;
using Quarry.Ingestion.Embeddings;
using Quarry.Ingestion.Tests.Fakes;
using Xunit;

namespace Quarry.Ingestion.Tests.Embeddings;

public class EmbeddingServiceTests
{
    [Fact]
    public async Task EmbedAsync_SingleChunk_AppliesPassagePrefixForE5()
    {
        var fakeGen = new FakeEmbeddingGenerator(dimensions: 384);
        var service = new EmbeddingService(
            generator: fakeGen,
            embeddingModel: "e5-test",
            dimensions: 384,
            prompt: EmbeddingPrompt.E5);

        var chunk = new DocumentChunk("c-1", "d-1", 0, "Тестовый текст");
        var embedded = await service.EmbedAsync(chunk);

        Assert.Equal("c-1", embedded.ChunkId);
        Assert.Equal("e5-test", embedded.EmbeddingModel);
        Assert.Equal(384, embedded.Vector.Length);
        Assert.Single(fakeGen.CapturedInputs);
        Assert.Equal("passage: Тестовый текст", fakeGen.CapturedInputs[0]);
    }

    [Fact]
    public async Task EmbedQueryAsync_AppliesQueryPrefixForE5()
    {
        var fakeGen = new FakeEmbeddingGenerator(dimensions: 384);
        var service = new EmbeddingService(
            generator: fakeGen,
            embeddingModel: "e5-test",
            dimensions: 384,
            prompt: EmbeddingPrompt.E5);

        var vector = await service.EmbedQueryAsync("Поисковый запрос");

        Assert.Equal(384, vector.Length);
        Assert.Single(fakeGen.CapturedInputs);
        Assert.Equal("query: Поисковый запрос", fakeGen.CapturedInputs[0]);
    }

    [Fact]
    public async Task EmbedAsync_DimensionMismatch_ThrowsInvalidOperationException()
    {
        var fakeGen = new FakeEmbeddingGenerator(dimensions: 128); // generator returns 128
        var service = new EmbeddingService(
            generator: fakeGen,
            embeddingModel: "test",
            dimensions: 384, // service expects 384
            prompt: EmbeddingPrompt.None);

        var chunk = new DocumentChunk("c-1", "d-1", 0, "текст");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EmbedAsync(chunk));
    }

    [Fact]
    public async Task EmbedAsync_Streaming_ProcessesAllChunks()
    {
        var fakeGen = new FakeEmbeddingGenerator(dimensions: 384);
        var service = new EmbeddingService(
            generator: fakeGen,
            embeddingModel: "test",
            dimensions: 384,
            prompt: EmbeddingPrompt.None);

        async IAsyncEnumerable<DocumentChunk> GetChunksAsync()
        {
            yield return new DocumentChunk("c-1", "d-1", 0, "текст 1");
            yield return new DocumentChunk("c-2", "d-1", 1, "текст 2");
            yield return new DocumentChunk("c-3", "d-1", 2, "текст 3");
            await Task.Yield();
        }

        var results = new List<EmbeddedChunk>();
        await foreach (var item in service.EmbedAsync(GetChunksAsync()))
        {
            results.Add(item);
        }

        Assert.Equal(3, results.Count);
        Assert.Equal("c-1", results[0].ChunkId);
        Assert.Equal("c-2", results[1].ChunkId);
        Assert.Equal("c-3", results[2].ChunkId);
    }
}
