namespace Quarry.Retrieval;

using Quarry.Core.Abstractions;
using Quarry.Core.Documents;

/// <summary>
/// Декоратор фильтрации по минимальному порогу релевантности (Score Floor).
/// Отсекает результаты ниже порога <see cref="MinScore"/>, предотвращая галлюцинации LLM
/// при отсутствии релевантных документов в корпусе.
/// </summary>
public sealed class MinScoreFilter : IRetriever
{
    private readonly IRetriever _inner;

    public double MinScore { get; }

    public MinScoreFilter(IRetriever inner, double minScore = 0.0)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        MinScore = minScore;
    }

    public string Strategy => $"min-score({_inner.Strategy})";

    public async Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        string query,
        int topK,
        CancellationToken cancellationToken = default)
    {
        var results = await _inner.RetrieveAsync(query, topK, cancellationToken).ConfigureAwait(false);

        if (MinScore <= 0.0)
        {
            return results;
        }

        return results.Where(r => r.Score >= MinScore).ToList();
    }
}
