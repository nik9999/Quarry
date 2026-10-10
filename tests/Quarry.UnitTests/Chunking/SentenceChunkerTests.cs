namespace Quarry.Ingestion.Tests.Chunking;

using Quarry.Core.Documents;
using Quarry.Ingestion.Chunking;
using Xunit;

public class SentenceChunkerTests
{
    [Fact]
    public async Task ChunkAsync_EmptyText_ReturnsZeroChunks()
    {
        var chunker = new SentenceChunker();
        var doc = new Document("doc-1", "   ");

        var chunks = await chunker.ChunkAsync(doc).ToListAsync();

        Assert.Empty(chunks);
    }

    [Fact]
    public async Task ChunkAsync_MultipleSentences_GroupsByMaxSize()
    {
        var chunker = new SentenceChunker(maxChunkSize: 60, overlapSentences: 0);
        var text = "Первое предложение. Второе предложение. Третье предложение длинное и важное.";
        var doc = new Document("doc-1", text);

        var chunks = await chunker.ChunkAsync(doc).ToListAsync();

        Assert.True(chunks.Count >= 2);
        foreach (var chunk in chunks)
        {
            Assert.Contains(chunk.Text, text);
            Assert.Equal(chunk.Text, text[chunk.StartCharOffset..chunk.EndCharOffset]);
        }
    }

    [Fact]
    public async Task ChunkAsync_SentenceOverlap_CarriesSentenceOver()
    {
        var chunker = new SentenceChunker(maxChunkSize: 45, overlapSentences: 1);
        var text = "Раз предложение. Два предложение. Три предложение.";
        var doc = new Document("doc-1", text);

        var chunks = await chunker.ChunkAsync(doc).ToListAsync();

        Assert.True(chunks.Count >= 2);
        // Second chunk should contain overlap from the first
        Assert.Contains("Два предложение", chunks[0].Text);
        Assert.Contains("Два предложение", chunks[1].Text);
    }
}
