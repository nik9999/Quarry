using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Quarry.Core.Abstractions;
using Quarry.Core.Documents;

namespace Quarry.Ingestion.Embeddings;

/// <summary>
/// Реализация <see cref="IEmbeddingService"/> на базе <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/> из Microsoft.Extensions.AI.
/// Позволяет подключать любой провайдер (ONNX, Ollama, OpenAI) без изменения вызывающего кода.
/// </summary>
public sealed class EmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _generator;
    private readonly EmbeddingPrompt _prompt;
    private readonly ILogger<EmbeddingService>? _logger;

    public string EmbeddingModel { get; }
    public int Dimensions { get; }

    public EmbeddingService(
        IEmbeddingGenerator<string, Embedding<float>> generator,
        string embeddingModel,
        int dimensions,
        EmbeddingPrompt? prompt = null,
        ILogger<EmbeddingService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentException.ThrowIfNullOrWhiteSpace(embeddingModel);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimensions);

        _generator = generator;
        EmbeddingModel = embeddingModel;
        Dimensions = dimensions;
        _prompt = prompt ?? EmbeddingPrompt.None;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<EmbeddedChunk> EmbedAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        string textToEmbed = _prompt.Apply(chunk.Text, EmbeddingTaskType.Document);
        var generated = await _generator.GenerateAsync(
            [textToEmbed],
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (generated.Count == 0)
        {
            throw new InvalidOperationException("Генератор эмбеддингов вернул пустой результат для чанка.");
        }

        var vector = generated[0].Vector;
        if (vector.Length != Dimensions)
        {
            throw new InvalidOperationException(
                $"Размерность сгенерированного вектора ({vector.Length}) не совпадает с ожидаемой ({Dimensions}).");
        }

        return new EmbeddedChunk(chunk, vector, EmbeddingModel);
    }

    /// <inheritdoc />
    public async Task<ReadOnlyMemory<float>> EmbedQueryAsync(string query, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        string textToEmbed = _prompt.Apply(query, EmbeddingTaskType.Query);
        var generated = await _generator.GenerateAsync(
            [textToEmbed],
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (generated.Count == 0)
        {
            throw new InvalidOperationException("Генератор эмбеддингов вернул пустой результат для запроса.");
        }

        var vector = generated[0].Vector;
        if (vector.Length != Dimensions)
        {
            throw new InvalidOperationException(
                $"Размерность сгенерированного вектора ({vector.Length}) не совпадает с ожидаемой ({Dimensions}).");
        }

        return vector;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<EmbeddedChunk> EmbedAsync(
        IAsyncEnumerable<DocumentChunk> chunks,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        await foreach (var chunk in chunks.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return await EmbedAsync(chunk, cancellationToken).ConfigureAwait(false);
        }
    }
}
