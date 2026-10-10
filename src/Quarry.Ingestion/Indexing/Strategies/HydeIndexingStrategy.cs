namespace Quarry.Ingestion.Indexing.Strategies;

using System.Runtime.CompilerServices;
using Quarry.Core.Abstractions;
using Quarry.Core.Documents;

/// <summary>
/// Стратегия индексации HyDE / генерации синтетических запросов: для каждого фрагмента генерирует гипотетические вопросы/запросы
/// и индексирует их в векторное пространство для максимального сближения пространства запросов и пространства документов.
/// </summary>
public sealed class HydeIndexingStrategy : IIndexingStrategy
{
    private readonly IChunker _chunker;
    private readonly Func<DocumentChunk, CancellationToken, Task<IReadOnlyList<string>>> _queryGenerator;
    private readonly EmbeddingPrompt? _prompt;

    public string StrategyName => $"hyde+{_chunker.Strategy}";

    public HydeIndexingStrategy(
        IChunker chunker,
        Func<DocumentChunk, CancellationToken, Task<IReadOnlyList<string>>> queryGenerator,
        EmbeddingPrompt? prompt = null)
    {
        _chunker = chunker ?? throw new ArgumentNullException(nameof(chunker));
        _queryGenerator = queryGenerator ?? throw new ArgumentNullException(nameof(queryGenerator));
        _prompt = prompt;
    }

    public async IAsyncEnumerable<IndexedItem> ProcessAsync(
        Document document,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var queryIndex = 0;

        await foreach (var chunk in _chunker.ChunkAsync(document, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Emit the primary chunk
            yield return new IndexedItem(chunk, _prompt, chunk.ChunkId, chunk.Metadata);

            // Generate synthetic queries
            var queries = await _queryGenerator(chunk, cancellationToken).ConfigureAwait(false);
            if (queries is not null && queries.Count > 0)
            {
                foreach (var q in queries)
                {
                    if (string.IsNullOrWhiteSpace(q))
                    {
                        continue;
                    }

                    var queryMeta = new Dictionary<string, string>();
                    if (chunk.Metadata is not null)
                    {
                        foreach (var (k, v) in chunk.Metadata)
                        {
                            queryMeta[k] = v;
                        }
                    }

                    queryMeta["is_synthetic_query"] = "true";
                    queryMeta["target_chunk_id"] = chunk.ChunkId;
                    queryMeta["target_text"] = chunk.Text;

                    var queryChunk = new DocumentChunk(
                        $"{chunk.ChunkId}#q_{queryIndex++}",
                        chunk.DocumentId,
                        chunk.Index,
                        q.Trim(),
                        queryMeta,
                        chunk.StartCharOffset,
                        chunk.EndCharOffset);

                    yield return new IndexedItem(queryChunk, _prompt, queryChunk.ChunkId, queryMeta);
                }
            }
        }
    }
}
