namespace Quarry.Ingestion.Tests.Chunking;

using Quarry.Core.Documents;
using Quarry.Ingestion.Chunking;
using Xunit;

public class RecursiveCharacterChunkerTests
{
    [Fact]
    public async Task ChunkAsync_EmptyText_ReturnsZeroChunks()
    {
        var chunker = new RecursiveCharacterChunker();
        var doc = new Document("doc-1", "");

        var chunks = await chunker.ChunkAsync(doc).ToListAsync();

        Assert.Empty(chunks);
    }

    [Fact]
    public async Task ChunkAsync_Paragraphs_SplitsOnParagraphsFirst()
    {
        var text = "Параграф один содержит вводную информацию о системе.\n\nПараграф два описывает детали архитектуры векторной базы данных.\n\nПараграф три содержит заключительные выводы и рекомендации.";
        var chunker = new RecursiveCharacterChunker(chunkSize: 80, chunkOverlap: 10);
        var doc = new Document("doc-1", text);

        var chunks = await chunker.ChunkAsync(doc).ToListAsync();

        Assert.NotEmpty(chunks);
        foreach (var chunk in chunks)
        {
            Assert.NotEmpty(chunk.Text);
            Assert.Equal(chunk.Text, text[chunk.StartCharOffset..chunk.EndCharOffset].Trim());
        }
    }

    [Fact]
    public async Task ChunkAsync_NoSeparators_ForceSplitsByCharacters()
    {
        var text = new string('x', 200);
        var chunker = new RecursiveCharacterChunker(chunkSize: 60, chunkOverlap: 10);
        var doc = new Document("doc-1", text);

        var chunks = await chunker.ChunkAsync(doc).ToListAsync();

        Assert.True(chunks.Count >= 3);
        foreach (var chunk in chunks)
        {
            Assert.True(chunk.Text.Length <= 60);
        }
    }
}
