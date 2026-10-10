namespace Quarry.Ingestion.Indexing.Strategies;

using System.Runtime.CompilerServices;
using Quarry.Core.Abstractions;
using Quarry.Core.Documents;

/// <summary>
/// Стандартная стратегия индексации: разбивает документ на чанки и передает каждый чанк на индексацию.
/// </summary>
public sealed class ChunkIndexingStrategy : IIndexingStrategy
{
    private readonly IChunker _chunker;
    private readonly EmbeddingPrompt? _prompt;

    public string StrategyName => $"chunk+{_chunker.Strategy}";

    public ChunkIndexingStrategy(IChunker chunker, EmbeddingPrompt? prompt = null)
    {
        _chunker = chunker ?? throw new ArgumentNullException(nameof(chunker));
        _prompt = prompt;
    }

    public async IAsyncEnumerable<IndexedItem> ProcessAsync(
        Document document,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        await foreach (var chunk in _chunker.ChunkAsync(document, cancellationToken).ConfigureAwait(false))
        {
            yield return new IndexedItem(chunk, _prompt, chunk.ChunkId, chunk.Metadata);
        }
    }
}
