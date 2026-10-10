namespace Quarry.Ingestion.Tests.Chunking;

using Microsoft.ML.Tokenizers;
using Quarry.Core.Documents;
using Quarry.Ingestion.Chunking;
using Xunit;

public class TokenAwareChunkerTests
{
    [Fact]
    public async Task ChunkAsync_SplitsByTokens_CorrectCount()
    {
        var tokenizer = TiktokenTokenizer.CreateForModel("gpt-4");
        var chunker = new TokenAwareChunker(tokenizer, maxTokens: 10, tokenOverlap: 2);

        var text = "Artificial intelligence and natural language processing are key technologies in modern vector retrieval pipelines and semantic search architectures.";
        var doc = new Document("doc-1", text);

        var chunks = await chunker.ChunkAsync(doc).ToListAsync();

        Assert.True(chunks.Count >= 2);
        foreach (var chunk in chunks)
        {
            Assert.NotEmpty(chunk.Text);
            Assert.NotNull(chunk.Metadata);
            Assert.True(chunk.Metadata.ContainsKey("token_count"));
            var tokenCount = int.Parse(chunk.Metadata["token_count"], System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(tokenCount <= 10);
            Assert.Equal(chunk.Text, text[chunk.StartCharOffset..chunk.EndCharOffset].Trim());
        }
    }

    [Fact]
    public async Task ChunkAsync_EmptyText_ReturnsZeroChunks()
    {
        var tokenizer = TiktokenTokenizer.CreateForModel("gpt-4");
        var chunker = new TokenAwareChunker(tokenizer, maxTokens: 10, tokenOverlap: 2);
        var doc = new Document("doc-1", "");

        var chunks = await chunker.ChunkAsync(doc).ToListAsync();

        Assert.Empty(chunks);
    }
}
