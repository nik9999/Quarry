using Quarry.Core.Documents;
using Xunit;

namespace Quarry.Core.Tests.Documents;

public class DocumentModelsTests
{
    [Fact]
    public void Document_StoresPropertiesCorrectly()
    {
        var meta = new Dictionary<string, string> { ["source"] = "wiki" };
        var doc = new Document("doc-1", "Full content", meta);

        Assert.Equal("doc-1", doc.Id);
        Assert.Equal("Full content", doc.Text);
        Assert.Equal("wiki", doc.Metadata?["source"]);
    }

    [Fact]
    public void DocumentChunk_StoresPropertiesCorrectly()
    {
        var meta = new Dictionary<string, string> { ["page"] = "1" };
        var chunk = new DocumentChunk("chunk-1", "doc-1", 0, "Chunk text", meta);

        Assert.Equal("chunk-1", chunk.ChunkId);
        Assert.Equal("doc-1", chunk.DocumentId);
        Assert.Equal(0, chunk.Index);
        Assert.Equal("Chunk text", chunk.Text);
        Assert.Equal("1", chunk.Metadata?["page"]);
        Assert.Equal(0, chunk.StartCharOffset);
        Assert.Equal(0, chunk.EndCharOffset);
    }

    [Fact]
    public void DocumentChunk_StoresOffsetsCorrectly()
    {
        var meta = new Dictionary<string, string> { ["section"] = "intro" };
        var chunk = new DocumentChunk("chunk-2", "doc-1", 1, "Sub text", 10, 18, meta);

        Assert.Equal("chunk-2", chunk.ChunkId);
        Assert.Equal("doc-1", chunk.DocumentId);
        Assert.Equal(1, chunk.Index);
        Assert.Equal("Sub text", chunk.Text);
        Assert.Equal(10, chunk.StartCharOffset);
        Assert.Equal(18, chunk.EndCharOffset);
        Assert.Equal("intro", chunk.Metadata?["section"]);
    }

    [Fact]
    public void EmbeddedChunk_ForwardsPropertiesFromChunk()
    {
        var chunk = new DocumentChunk("chunk-1", "doc-1", 0, "Chunk text");
        float[] vector = [0.1f, 0.2f, 0.3f];
        var embedded = new EmbeddedChunk(chunk, vector, "model-test");

        Assert.Equal("chunk-1", embedded.ChunkId);
        Assert.Equal("doc-1", embedded.DocumentId);
        Assert.Equal("Chunk text", embedded.Text);
        Assert.Equal("model-test", embedded.EmbeddingModel);
        Assert.Equal(3, embedded.Vector.Length);
    }

    [Fact]
    public void RetrievalResult_ForwardsPropertiesFromChunk()
    {
        var chunk = new DocumentChunk("c-10", "d-5", 2, "Result text");
        var result = new RetrievalResult(chunk, 0.95);

        Assert.Equal("c-10", result.ChunkId);
        Assert.Equal("d-5", result.DocumentId);
        Assert.Equal("Result text", result.Text);
        Assert.Equal(0.95, result.Score);
    }
}
