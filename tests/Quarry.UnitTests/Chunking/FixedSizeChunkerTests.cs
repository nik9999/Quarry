namespace Quarry.Ingestion.Tests.Chunking;

using Quarry.Core.Documents;
using Quarry.Ingestion.Chunking;
using Xunit;

public class FixedSizeChunkerTests
{
    [Fact]
    public async Task ChunkAsync_EmptyText_ReturnsZeroChunks()
    {
        var chunker = new FixedSizeChunker(100, 20);
        var doc = new Document("doc-1", "");

        var chunks = await chunker.ChunkAsync(doc).ToListAsync();

        Assert.Empty(chunks);
    }

    [Fact]
    public async Task ChunkAsync_ShortText_ReturnsSingleChunk()
    {
        var chunker = new FixedSizeChunker(100, 20);
        var doc = new Document("doc-1", "Короткий текст");

        var chunks = await chunker.ChunkAsync(doc).ToListAsync();

        Assert.Single(chunks);
        Assert.Equal("Короткий текст", chunks[0].Text);
        Assert.Equal(0, chunks[0].StartCharOffset);
        Assert.Equal("Короткий текст".Length, chunks[0].EndCharOffset);
        Assert.Equal("doc-1#0", chunks[0].ChunkId);
    }

    [Fact]
    public async Task ChunkAsync_LongText_ChunksWithOverlap()
    {
        var chunker = new FixedSizeChunker(50, 10);
        var text = new string('A', 120);
        var doc = new Document("doc-1", text);

        var chunks = await chunker.ChunkAsync(doc).ToListAsync();

        Assert.True(chunks.Count >= 3);
        foreach (var chunk in chunks)
        {
            Assert.True(chunk.Text.Length <= 50);
            Assert.Equal(chunk.Text, text[chunk.StartCharOffset..chunk.EndCharOffset]);
        }

        // Verify overlap between chunk 0 and chunk 1
        Assert.Equal(0, chunks[0].StartCharOffset);
        Assert.Equal(50, chunks[0].EndCharOffset);
        Assert.Equal(40, chunks[1].StartCharOffset); // 50 - 10
    }

    [Fact]
    public void Constructor_InvalidArguments_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedSizeChunker(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedSizeChunker(100, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedSizeChunker(100, -1));
    }
}
