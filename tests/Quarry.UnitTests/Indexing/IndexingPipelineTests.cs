namespace Quarry.Ingestion.Tests.Indexing;

using Quarry.Core.Abstractions;
using Quarry.Core.Documents;
using Quarry.Ingestion.Chunking;
using Quarry.Ingestion.Embeddings;
using Quarry.Ingestion.Indexing;
using Quarry.Ingestion.Tests.Fakes;
using Xunit;

public class IndexingPipelineTests
{
    [Fact]
    public async Task IngestAsync_EndToEnd_EmbedsAndUpsertsToVectorStore()
    {
        var fakeGenerator = new FakeEmbeddingGenerator(dimensions: 384);
        var embeddingService = new EmbeddingService(fakeGenerator, "test-model", 384);
        var vectorStore = new FakeVectorStore();
        var chunker = new FixedSizeChunker(50, 10);

        var pipeline = new IndexingPipelineBuilder()
            .WithChunker(chunker)
            .WithEmbeddingService(embeddingService)
            .WithVectorStore(vectorStore)
            .WithBatchSize(2)
            .Build();

        var docs = new List<Document>
        {
            new("doc-1", "Первый документ для проверки полного конвейера индексации."),
            new("doc-2", "Второй документ с дополнительной тестовой информацией.")
        };

        var result = await pipeline.IngestAsync(docs);

        Assert.Equal(2, result.TotalDocuments);
        Assert.True(result.TotalChunksIndexed >= 2);
        Assert.True(vectorStore.CollectionExistsCalled);
        Assert.Equal(result.TotalChunksIndexed, vectorStore.UpsertedChunks.Count);
    }

    [Fact]
    public void Builder_MissingDependencies_ThrowsInvalidOperationException()
    {
        var builder = new IndexingPipelineBuilder();
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }
}
