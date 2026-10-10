namespace Quarry.Ingestion.Tests.Chunking;

using Quarry.Core.Abstractions;
using Quarry.Core.Documents;
using Quarry.Ingestion.Chunking;
using Xunit;

public class SemanticChunkerTests
{
    private sealed class FakeEmbeddingService : IEmbeddingService
    {
        public string EmbeddingModel => "fake-model";
        public int Dimensions => 3;

        public Task<EmbeddedChunk> EmbedAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
        {
            var vector = GetVector(chunk.Text);
            return Task.FromResult(new EmbeddedChunk(chunk, vector, EmbeddingModel));
        }

        public Task<ReadOnlyMemory<float>> EmbedQueryAsync(string query, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ReadOnlyMemory<float>>(GetVector(query));
        }

        public async IAsyncEnumerable<EmbeddedChunk> EmbedAsync(
            IAsyncEnumerable<DocumentChunk> chunks,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var chunk in chunks.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                yield return new EmbeddedChunk(chunk, GetVector(chunk.Text), EmbeddingModel);
            }
        }

        private static float[] GetVector(string text)
        {
            if (text.Contains("космос", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("ракета", StringComparison.OrdinalIgnoreCase))
            {
                return [1.0f, 0.0f, 0.0f];
            }

            if (text.Contains("океан", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("рыба", StringComparison.OrdinalIgnoreCase))
            {
                return [0.0f, 1.0f, 0.0f];
            }

            return [0.577f, 0.577f, 0.577f];
        }
    }

    [Fact]
    public async Task ChunkAsync_DetectsSemanticBoundaries()
    {
        var embeddingService = new FakeEmbeddingService();
        var chunker = new SemanticChunker(embeddingService, similarityThreshold: 0.5f, minSentencesPerChunk: 1, maxSentencesPerChunk: 4);

        var text = "Космос полон звезд. Ракета летит к марсу. Океан очень глубокий. Рыба плывет по течению.";
        var doc = new Document("doc-1", text);

        var chunks = await chunker.ChunkAsync(doc).ToListAsync();

        Assert.Equal(2, chunks.Count);
        Assert.Contains("Космос", chunks[0].Text);
        Assert.Contains("Ракета", chunks[0].Text);
        Assert.Contains("Океан", chunks[1].Text);
        Assert.Contains("Рыба", chunks[1].Text);
    }

    [Fact]
    public async Task ChunkAsync_EmptyText_ReturnsZeroChunks()
    {
        var embeddingService = new FakeEmbeddingService();
        var chunker = new SemanticChunker(embeddingService);
        var doc = new Document("doc-1", "");

        var chunks = await chunker.ChunkAsync(doc).ToListAsync();

        Assert.Empty(chunks);
    }
}
