namespace Quarry.Ingestion.Indexing.Strategies;

using System.Runtime.CompilerServices;
using Quarry.Core.Abstractions;
using Quarry.Core.Documents;

/// <summary>
/// Стратегия индексации сводки (Summary): генерирует обобщенную сводку документа для глобального макро-поиска по всему корпусу.
/// </summary>
public sealed class SummaryIndexingStrategy : IIndexingStrategy
{
    private readonly Func<Document, CancellationToken, Task<string>> _summaryGenerator;
    private readonly IChunker? _detailChunker;
    private readonly EmbeddingPrompt? _prompt;

    public string StrategyName => _detailChunker is not null ? $"summary+detail({_detailChunker.Strategy})" : "summary";

    public SummaryIndexingStrategy(
        Func<Document, CancellationToken, Task<string>> summaryGenerator,
        IChunker? detailChunker = null,
        EmbeddingPrompt? prompt = null)
    {
        _summaryGenerator = summaryGenerator ?? throw new ArgumentNullException(nameof(summaryGenerator));
        _detailChunker = detailChunker;
        _prompt = prompt;
    }

    public async IAsyncEnumerable<IndexedItem> ProcessAsync(
        Document document,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var summaryText = await _summaryGenerator(document, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(summaryText))
        {
            var summaryMeta = new Dictionary<string, string>();
            if (document.Metadata is not null)
            {
                foreach (var (k, v) in document.Metadata)
                {
                    summaryMeta[k] = v;
                }
            }

            summaryMeta["is_summary"] = "true";

            var summaryChunk = new DocumentChunk(
                $"{document.Id}#summary",
                document.Id,
                0,
                summaryText.Trim(),
                summaryMeta,
                0,
                document.Text.Length);

            yield return new IndexedItem(summaryChunk, _prompt, summaryChunk.ChunkId, summaryMeta);
        }

        if (_detailChunker is not null)
        {
            await foreach (var detail in _detailChunker.ChunkAsync(document, cancellationToken).ConfigureAwait(false))
            {
                yield return new IndexedItem(detail, _prompt, detail.ChunkId, detail.Metadata);
            }
        }
    }
}
