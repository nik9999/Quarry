namespace Quarry.Retrieval;

using Quarry.Core.Documents;
using Quarry.Core.Numerics;

/// <summary>
/// Кандидат для диверсификации выборки по алгоритму MMR.
/// </summary>
public sealed record MmrCandidate(
    DocumentChunk Chunk,
    ReadOnlyMemory<float> Vector,
    double Relevance);

/// <summary>
/// Выборка по алгоритму Maximal Marginal Relevance (MMR, Carbonell & Goldstein 1998).
/// Жадным образом выбирает кандидатов, максимизирующих λ·rel(d) − (1−λ)·max_{s∈selected} cosine(d, s),
/// обеспечивая баланс между релевантностью и разнообразием фрагментов и исключая дубликаты.
/// </summary>
public static class MmrSelector
{
    /// <summary>
    /// Выбирает до <paramref name="k"/> кандидатов в порядке MMR.
    /// </summary>
    /// <param name="candidates">Пул кандидатов.</param>
    /// <param name="k">Максимальное количество результатов.</param>
    /// <param name="lambda">Коэффициент баланса релевантность/разнообразие в диапазоне [0, 1].</param>
    public static IReadOnlyList<RetrievalResult> Select(
        IReadOnlyList<MmrCandidate> candidates,
        int k,
        double lambda = 0.5)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (k <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(k), "k must be greater than zero.");
        }

        if (lambda is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(lambda), "lambda must be in range [0, 1].");
        }

        if (candidates.Count == 0)
        {
            return [];
        }

        var remaining = new List<MmrCandidate>(candidates);
        var selected = new List<MmrCandidate>(Math.Min(k, remaining.Count));
        var take = Math.Min(k, remaining.Count);

        while (selected.Count < take)
        {
            var bestIndex = -1;
            var bestScore = double.NegativeInfinity;

            for (var i = 0; i < remaining.Count; i++)
            {
                var candidate = remaining[i];

                double maxSimToSelected = 0.0;
                for (var s = 0; s < selected.Count; s++)
                {
                    var sim = CosineKernel.CosineSimilarity(candidate.Vector.Span, selected[s].Vector.Span);
                    if (sim > maxSimToSelected)
                    {
                        maxSimToSelected = sim;
                    }
                }

                var mmr = (lambda * candidate.Relevance) - ((1.0 - lambda) * maxSimToSelected);
                if (mmr > bestScore)
                {
                    bestScore = mmr;
                    bestIndex = i;
                }
            }

            selected.Add(remaining[bestIndex]);
            remaining.RemoveAt(bestIndex);
        }

        return selected.Select(c => new RetrievalResult(c.Chunk, c.Relevance)).ToList();
    }
}
