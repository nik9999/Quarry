using Microsoft.Extensions.AI;

namespace Quarry.Ingestion.Tests.Fakes;

public sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly int _dimensions;
    public List<string> CapturedInputs { get; } = new();

    public FakeEmbeddingGenerator(int dimensions = 384)
    {
        _dimensions = dimensions;
    }

    public EmbeddingGeneratorMetadata Metadata => new("fake-model");

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var result = new GeneratedEmbeddings<Embedding<float>>();

        foreach (var value in values)
        {
            CapturedInputs.Add(value);
            var vector = new float[_dimensions];
            // Fill with dummy normalized values
            Array.Fill(vector, 1.0f / MathF.Sqrt(_dimensions));
            result.Add(new Embedding<float>(vector));
        }

        return Task.FromResult(result);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose() { }
}
