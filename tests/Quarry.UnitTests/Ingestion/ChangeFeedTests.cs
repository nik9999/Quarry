namespace Quarry.Ingestion.Tests.Events;

using Quarry.Core.Documents;
using Quarry.Ingestion.Chunking;
using Quarry.Ingestion.Embeddings;
using Quarry.Ingestion.Events;
using Quarry.Ingestion.Indexing;
using Quarry.Ingestion.Tests.Fakes;
using Xunit;

public class ChangeFeedTests
{
    [Fact]
    public async Task ChannelChangeFeed_PublishAndRead_DeliversEvents()
    {
        var feed = new ChannelChangeFeed(capacity: 10);
        var doc = new Document("doc-1", "Текст события.");
        var change = new DocumentChanged("doc-1", DocumentChangeType.Created, DateTimeOffset.UtcNow, doc);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await feed.PublishChangeAsync(change, cts.Token);

        await foreach (var received in feed.ReadChangesAsync(cts.Token))
        {
            Assert.Equal("doc-1", received.DocumentId);
            Assert.Equal(DocumentChangeType.Created, received.ChangeType);
            Assert.Equal(doc, received.Document);
            break;
        }
    }

    [Fact]
    public async Task DocumentReingestService_ProcessesCreatedAndDeletedEvents()
    {
        var feed = new ChannelChangeFeed(capacity: 10);
        var fakeGenerator = new FakeEmbeddingGenerator(dimensions: 384);
        var embeddingService = new EmbeddingService(fakeGenerator, "test-model", 384);
        var vectorStore = new FakeVectorStore();
        var chunker = new FixedSizeChunker(50, 10);

        var pipeline = new IndexingPipelineBuilder()
            .WithChunker(chunker)
            .WithEmbeddingService(embeddingService)
            .WithVectorStore(vectorStore)
            .Build();

        using var service = new DocumentReingestService(feed, pipeline, vectorStore);
        using var cts = new CancellationTokenSource();

        var runTask = service.StartAsync(cts.Token);

        // Publish Created event
        var doc = new Document("reingest-1", "Новый документ для фонового реинжеста.");
        await feed.PublishChangeAsync(new DocumentChanged("reingest-1", DocumentChangeType.Created, DateTimeOffset.UtcNow, doc));

        // Publish Deleted event
        await feed.PublishChangeAsync(new DocumentChanged("reingest-1", DocumentChangeType.Deleted, DateTimeOffset.UtcNow));

        // Allow background service to process
        for (var i = 0; i < 40 && (vectorStore.UpsertedChunks.Count == 0 || vectorStore.DeletedChunkIds.Count == 0); i++)
        {
            await Task.Delay(50);
        }

        await service.StopAsync(CancellationToken.None);
        cts.Cancel();
        await runTask;

        Assert.NotEmpty(vectorStore.UpsertedChunks);
        Assert.Contains("reingest-1", vectorStore.DeletedChunkIds);
    }
}
