using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Quarry.Core.Abstractions;
using Quarry.Core.Documents;

namespace Quarry.Ingestion.Embeddings;

/// <summary>
/// Пакетный конвейер векторизации для эффективной обработки больших объемов чанков.
/// Объединяет чанки в батчи для минимизации накладных расходов вызова нейросетевых моделей.
/// </summary>
public sealed class BatchEmbeddingPipeline
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _generator;
    private readonly EmbeddingPrompt _prompt;
    private readonly string _embeddingModel;
    private readonly int _batchSize;
    private readonly ILogger<BatchEmbeddingPipeline>? _logger;

    public BatchEmbeddingPipeline(
        IEmbeddingGenerator<string, Embedding<float>> generator,
        string embeddingModel,
        int batchSize = 32,
        EmbeddingPrompt? prompt = null,
        ILogger<BatchEmbeddingPipeline>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentException.ThrowIfNullOrWhiteSpace(embeddingModel);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        _generator = generator;
        _embeddingModel = embeddingModel;
        _batchSize = batchSize;
        _prompt = prompt ?? EmbeddingPrompt.None;
        _logger = logger;
    }

    /// <summary>
    /// Выполняет пакетную векторизацию списка чанков.
    /// </summary>
    public async Task<IReadOnlyList<EmbeddedChunk>> EmbedBatchAsync(
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        if (chunks.Count == 0) return Array.Empty<EmbeddedChunk>();

        var results = new List<EmbeddedChunk>(chunks.Count);

        for (int i = 0; i < chunks.Count; i += _batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentBatch = chunks.Skip(i).Take(_batchSize).ToList();
            var inputTexts = currentBatch.Select(c => _prompt.Apply(c.Text, EmbeddingTaskType.Document)).ToList();

            var embeddings = await _generator.GenerateAsync(inputTexts, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            for (int j = 0; j < currentBatch.Count; j++)
            {
                results.Add(new EmbeddedChunk(currentBatch[j], embeddings[j].Vector, _embeddingModel));
            }
        }

        return results;
    }

    /// <summary>
    /// Потоковая пакетная векторизация.
    /// </summary>
    public async IAsyncEnumerable<EmbeddedChunk> EmbedStreamAsync(
        IAsyncEnumerable<DocumentChunk> chunks,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        var buffer = new List<DocumentChunk>(_batchSize);

        await foreach (var chunk in chunks.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            buffer.Add(chunk);
            if (buffer.Count >= _batchSize)
            {
                var processed = await EmbedBatchAsync(buffer, cancellationToken).ConfigureAwait(false);
                foreach (var item in processed)
                {
                    yield return item;
                }
                buffer.Clear();
            }
        }

        if (buffer.Count > 0)
        {
            var processed = await EmbedBatchAsync(buffer, cancellationToken).ConfigureAwait(false);
            foreach (var item in processed)
            {
                yield return item;
            }
            buffer.Clear();
        }
    }
}
