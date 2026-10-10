namespace Quarry.Retrieval;

using Quarry.Core.Documents;

/// <summary>
/// Переупорядочивание контекста для компенсации эффекта "Lost in the middle" (Liu et al. 2023).
/// LLM обращают наибольшее внимание на начало и конец контекстного окна промпта.
/// Метод <see cref="Interleave"/> распределяет наиболее релевантные чанки по краям (в начало и конец).
/// </summary>
public static class ContextOrdering
{
    /// <summary>
    /// Переупорядочивает список результатов, отсортированный по убыванию релевантности.
    /// Четные ранги (0, 2, 4...) направляются в начало, нечетные (1, 3, 5...) — в конец списка.
    /// Например: [1, 2, 3, 4, 5] превращается в [1, 3, 5, 4, 2].
    /// </summary>
    public static IReadOnlyList<RetrievalResult> Interleave(
        IReadOnlyList<RetrievalResult> rankedByRelevance)
    {
        ArgumentNullException.ThrowIfNull(rankedByRelevance);

        var n = rankedByRelevance.Count;
        if (n == 0)
        {
            return [];
        }

        var result = new RetrievalResult[n];
        var front = 0;
        var back = n - 1;

        for (var i = 0; i < n; i++)
        {
            if ((i & 1) == 0)
            {
                result[front++] = rankedByRelevance[i];
            }
            else
            {
                result[back--] = rankedByRelevance[i];
            }
        }

        return result;
    }
}
