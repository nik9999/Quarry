namespace Quarry.Ingestion.Tests.Chunking;

using Quarry.Core.Documents;
using Quarry.Ingestion.Chunking;
using Xunit;

public class CodeFileChunkerTests
{
    [Fact]
    public async Task ChunkAsync_ParsesClassesAndMethods()
    {
        var code = """
        namespace MyNamespace;

        public class Calculator
        {
            public int Add(int a, int b)
            {
                return a + b;
            }

            public int Multiply(int a, int b)
            {
                return a * b;
            }
        }
        """;

        var chunker = new CodeFileChunker(maxChunkSize: 100); // Forces member splitting
        var doc = new Document("code-1", code);

        var chunks = await chunker.ChunkAsync(doc).ToListAsync();

        Assert.NotEmpty(chunks);
        Assert.Contains(chunks, c => c.Metadata?["symbol_name"] == "Add");
        Assert.Contains(chunks, c => c.Metadata?["symbol_name"] == "Multiply");
        Assert.All(chunks, c => Assert.Equal("code-1", c.DocumentId));
    }

    [Fact]
    public async Task ChunkAsync_SmallClass_EmittedAsSingleChunk()
    {
        var code = """
        public class Point
        {
            public int X { get; set; }
            public int Y { get; set; }
        }
        """;

        var chunker = new CodeFileChunker(maxChunkSize: 500);
        var doc = new Document("code-2", code);

        var chunks = await chunker.ChunkAsync(doc).ToListAsync();

        Assert.Single(chunks);
        Assert.Equal("Point", chunks[0].Metadata?["symbol_name"]);
        Assert.Contains("public class Point", chunks[0].Text);
    }

    [Fact]
    public async Task ChunkAsync_EmptyText_ReturnsZeroChunks()
    {
        var chunker = new CodeFileChunker();
        var doc = new Document("code-empty", "");

        var chunks = await chunker.ChunkAsync(doc).ToListAsync();

        Assert.Empty(chunks);
    }
}
