namespace Quarry.Retrieval.Decorators;

using Quarry.Core.Abstractions;
using Quarry.Core.Documents;

/// <summary>
/// Декоратор Corrective RAG (CRAG, Yan et al. 2024): оценивает уверенность и релевантность результатов поиска.
/// При высокой уверенности возвращает документы; при низкой — обращается к запасному источнику (fallback) или воздерживается от ответа;
/// при средней — объединяет или фильтрует неоднозначные фрагменты.
/// </summary>
public sealed class CragRetriever : IRetriever
{
    private readonly IRetriever _inner;
    private readonly IRetriever? _fallback;
    private readonly double _upperThreshold;
    private readonly double _lowerThreshold;

    public double UpperThreshold => _upperThreshold;
    public double LowerThreshold => _lowerThreshold;

    public CragRetriever(
        IRetriever inner,
        IRetriever? fallback = null,
        double upperThreshold = 0.70,
        double lowerThreshold = 0.35)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _fallback = fallback;

        if (upperThreshold < lowerThreshold)
        {
            throw new ArgumentException("Upper threshold must be greater than or equal to lower threshold.");
        }

        _upperThreshold = upperThreshold;
        _lowerThreshold = lowerThreshold;
    }

    public string Strategy => $"crag({_inner.Strategy})";

    public async Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        string query,
        int topK,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);

        var primaryResults = await _inner.RetrieveAsync(query, topK, cancellationToken).ConfigureAwait(false);

        var bestScore = primaryResults.Count > 0 ? primaryResults.Max(r => r.Score) : 0.0;

        // Состояние 1: Correct — высокая релевантность
        if (bestScore >= _upperThreshold)
        {
            return primaryResults;
        }

        // Состояние 2: Incorrect — низкая релевантность (шум)
        if (bestScore < _lowerThreshold)
        {
            if (_fallback is not null)
            {
                return await _fallback.RetrieveAsync(query, topK, cancellationToken).ConfigureAwait(false);
            }

            // Без фоллбэка отсекаем нерелевантные данные для предотвращения галлюцинаций
            return [];
        }

        // Состояние 3: Ambiguous — средняя уверенность
        if (_fallback is not null)
        {
            var fallbackResults = await _fallback.RetrieveAsync(query, topK, cancellationToken).ConfigureAwait(false);
            var merger = new RrfMerger();
            return merger.Merge(primaryResults, fallbackResults, topK);
        }

        return primaryResults.Where(r => r.Score >= _lowerThreshold).ToList();
    }
}
