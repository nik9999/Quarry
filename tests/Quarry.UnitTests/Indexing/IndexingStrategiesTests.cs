namespace Quarry.Ingestion.Tests.Indexing;

using Quarry.Core.Abstractions;
using Quarry.Core.Documents;
using Quarry.Ingestion.Chunking;
using Quarry.Ingestion.Indexing.Strategies;
using Xunit;

public class IndexingStrategiesTests
{
    [Fact]
    public async Task ChunkIndexingStrategy_EmitsIndexedItems()
    {
        var chunker = new FixedSizeChunker(50, 10);
        var strategy = new ChunkIndexingStrategy(chunker, EmbeddingPrompt.E5);

        var doc = new Document("doc-1", "Текст для проверки стандартной стратегии индексации.");
        var items = await strategy.ProcessAsync(doc).ToListAsync();

        Assert.NotEmpty(items);
        Assert.All(items, item =>
        {
            Assert.Equal("doc-1", item.Chunk.DocumentId);
            Assert.Equal(EmbeddingPrompt.E5, item.Prompt);
        });
    }

    [Fact]
    public async Task SubChunkIndexingStrategy_ParentChildLinking_CorrectMetadata()
    {
        var parentChunker = new FixedSizeChunker(100, 0);
        var childChunker = new FixedSizeChunker(30, 0);
        var strategy = new SubChunkIndexingStrategy(parentChunker, childChunker, indexParentAsWell: true);

        var doc = new Document("doc-parent", new string('X', 150));
        var items = await strategy.ProcessAsync(doc).ToListAsync();

        Assert.True(items.Count >= 4);
        var parentItems = items.Where(i => i.Metadata?.ContainsKey("is_parent") == true).ToList();
        var childItems = items.Where(i => i.Metadata?.ContainsKey("parent_id") == true).ToList();

        Assert.NotEmpty(parentItems);
        Assert.NotEmpty(childItems);

        foreach (var child in childItems)
        {
            Assert.NotNull(child.Metadata?["parent_id"]);
            Assert.NotNull(child.Metadata?["parent_text"]);
            Assert.NotNull(child.Metadata?["child_subindex"]);
        }
    }

    [Fact]
    public async Task HydeIndexingStrategy_GeneratesSyntheticQueries()
    {
        var chunker = new FixedSizeChunker(50, 0);
        var strategy = new HydeIndexingStrategy(
            chunker,
            (chunk, _) => Task.FromResult<IReadOnlyList<string>>(new[] { $"Что такое {chunk.ChunkId}?", $"Как устроен {chunk.ChunkId}?" }));

        var doc = new Document("doc-hyde", "Текст для HyDE генерации запросов.");
        var items = await strategy.ProcessAsync(doc).ToListAsync();

        Assert.True(items.Count >= 3); // 1 primary chunk + 2 synthetic queries
        var queries = items.Where(i => i.Metadata?.ContainsKey("is_synthetic_query") == true).ToList();
        Assert.Equal(2, queries.Count);
        Assert.Contains("Что такое doc-hyde#0?", queries[0].Chunk.Text);
    }

    [Fact]
    public async Task SummaryIndexingStrategy_GeneratesSummaryItem()
    {
        var detailChunker = new FixedSizeChunker(50, 0);
        var strategy = new SummaryIndexingStrategy(
            (doc, _) => Task.FromResult($"Сводка документа {doc.Id}"),
            detailChunker);

        var doc = new Document("doc-sum", "Подробный контент документа.");
        var items = await strategy.ProcessAsync(doc).ToListAsync();

        Assert.Contains(items, i => i.Metadata?.ContainsKey("is_summary") == true && i.Chunk.Text == "Сводка документа doc-sum");
        Assert.Contains(items, i => i.Metadata?.ContainsKey("is_summary") != true);
    }
}
