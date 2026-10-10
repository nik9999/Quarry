namespace Quarry.Retrieval.Decorators;

using Microsoft.Extensions.AI;
using Quarry.Core.Abstractions;
using Quarry.Core.Documents;

/// <summary>
/// Декоратор RAG-Fusion (Rackauckas 2024): генерирует несколько вариаций и перефразировок исходного запроса через LLM,
/// исполняет их параллельно и объединяет все потоки результатов через алгоритм взаимного ранга RRF.
/// </summary>
public sealed class RagFusionRetriever : IRetriever
{
    private readonly IRetriever _inner;
    private readonly IChatClient _chat;
    private readonly RrfMerger _merger;
    private readonly int _queryCount;

    public RagFusionRetriever(
        IRetriever inner,
        IChatClient chat,
        RrfMerger? merger = null,
        int queryCount = 3)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));
        _merger = merger ?? new RrfMerger();

        if (queryCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(queryCount), "queryCount must be greater than zero.");
        }

        _queryCount = queryCount;
    }

    public string Strategy => $"rag-fusion({_inner.Strategy})";

    public async Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        string query,
        int topK,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);

        var queries = new List<string> { query };

        try
        {
            var prompt =
                $"Сгенерируйте {_queryCount} различных поисковых формулировок и альтернативных вариантов для следующего вопроса. " +
                $"Каждый вариант пишите с новой строки без нумерации и лишних символов.\n\nВопрос: {query}";

            var response = await _chat.GetResponseAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(response.Text))
            {
                var lines = response.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var line in lines)
                {
                    if (line.Length > 0 && !queries.Contains(line, StringComparer.OrdinalIgnoreCase))
                    {
                        queries.Add(line);
                    }
                }
            }
        }
        catch (Exception)
        {
            // Fallback: при сбое LLM остаемся только с оригинальным запросом
        }

        var candidateK = Math.Max(topK * 2, topK + 5);
        var tasks = queries.Select(q => _inner.RetrieveAsync(q, candidateK, cancellationToken)).ToList();

        await Task.WhenAll(tasks).WaitAsync(cancellationToken).ConfigureAwait(false);

        var resultLists = tasks.Select(t => t.Result).ToList();
        return _merger.Merge(resultLists, topK);
    }
}
