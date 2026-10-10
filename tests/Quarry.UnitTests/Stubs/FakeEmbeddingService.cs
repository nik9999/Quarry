namespace Quarry.Retrieval.Tests.Fakes;

using Quarry.Core.Abstractions;
using Quarry.Core.Documents;

public sealed class FakeEmbeddingService : IEmbeddingService
{
    private readonly float[] _fixedVector;

    public string EmbeddingModel => "fake-embedding-model";
    public int Dimensions => _fixedVector.Length;
    public string? LastQuery { get; private set; }

    public FakeEmbeddingService(int dimensions = 384)
    {
        _fixedVector = new float[dimensions];
        _fixedVector[0] = 1.0f;
    }

    public Task<EmbeddedChunk> EmbedAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new EmbeddedChunk(chunk, _fixedVector, EmbeddingModel));
    }

    public Task<ReadOnlyMemory<float>> EmbedQueryAsync(string query, CancellationToken cancellationToken = default)
    {
        LastQuery = query;
        return Task.FromResult<ReadOnlyMemory<float>>(_fixedVector);
    }

    public async IAsyncEnumerable<EmbeddedChunk> EmbedAsync(
        IAsyncEnumerable<DocumentChunk> chunks,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var chunk in chunks.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return new EmbeddedChunk(chunk, _fixedVector, EmbeddingModel);
        }
    }
}
