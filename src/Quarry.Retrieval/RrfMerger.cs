namespace Quarry.Retrieval;

using Quarry.Core.Documents;

/// <summary>
/// Слияние ранжированных списков по алгоритму Reciprocal Rank Fusion (RRF, Cormack et al. 2009).
/// Объединяет списки результатов от разных ретриверов путем суммирования 1 / (K + rank).
/// Устойчив к различиям в шкалах оценок между ретриверами.
/// </summary>
public sealed class RrfMerger
{
    public int K { get; }

    public RrfMerger(int k = 60)
    {
        if (k <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(k), "K must be greater than zero.");
        }

        K = k;
    }

    /// <summary>
    /// Объединяет два списка результатов по их взаимному рангу.
    /// </summary>
    public IReadOnlyList<RetrievalResult> Merge(
        IReadOnlyList<RetrievalResult> a,
        IReadOnlyList<RetrievalResult> b,
        int topK)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return Merge([a, b], topK);
    }

    /// <summary>
    /// Объединяет произвольное количество ранжированных списков результатов по алгоритму RRF.
    /// </summary>
    public IReadOnlyList<RetrievalResult> Merge(
        IEnumerable<IReadOnlyList<RetrievalResult>> lists,
        int topK)
    {
        ArgumentNullException.ThrowIfNull(lists);
        if (topK <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(topK), "topK must be greater than zero.");
        }

        var fused = new Dictionary<string, (DocumentChunk Chunk, double Score)>(StringComparer.Ordinal);

        foreach (var list in lists)
        {
            for (var rank = 0; rank < list.Count; rank++)
            {
                var item = list[rank];
                var key = item.Chunk.ChunkId;
                var contribution = 1.0 / (K + rank + 1);

                if (fused.TryGetValue(key, out var existing))
                {
                    fused[key] = (existing.Chunk, existing.Score + contribution);
                }
                else
                {
                    fused[key] = (item.Chunk, contribution);
                }
            }
        }

        var results = fused.Values
            .Select(v => new RetrievalResult(v.Chunk, v.Score))
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .ToList();

        return results;
    }
}
