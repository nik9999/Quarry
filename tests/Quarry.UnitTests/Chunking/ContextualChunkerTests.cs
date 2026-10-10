namespace Quarry.Ingestion.Tests.Chunking;

using Quarry.Core.Documents;
using Quarry.Ingestion.Chunking;
using Xunit;

public class ContextualChunkerTests
{
    [Fact]
    public async Task ChunkAsync_EnrichesChunksWithContext()
    {
        var inner = new FixedSizeChunker(50, 10);
        var contextual = new ContextualChunker(inner, (doc, chunk) => $"Context: Document {doc.Id}");

        var doc = new Document("doc-1", "Тестовый текст для проверки контекстуализации фрагментов.");
        var chunks = await contextual.ChunkAsync(doc).ToListAsync();

        Assert.NotEmpty(chunks);
        foreach (var chunk in chunks)
        {
            Assert.StartsWith("Context: Document doc-1", chunk.Text);
            Assert.Equal("true", chunk.Metadata?["is_contextual"]);
            Assert.Equal("Context: Document doc-1", chunk.Metadata?["context_prefix"]);
            // Original offsets are preserved
            Assert.True(chunk.EndCharOffset > chunk.StartCharOffset);
        }
    }
}
