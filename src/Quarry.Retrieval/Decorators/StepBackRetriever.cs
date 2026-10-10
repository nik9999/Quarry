namespace Quarry.Retrieval.Decorators;

using Microsoft.Extensions.AI;
using Quarry.Core.Abstractions;
using Quarry.Core.Documents;

/// <summary>
/// Декоратор Step-Back Prompting (Zheng et al. 2023): формулирует более общий / концептуальный вопрос
/// более высокого уровня абстракции, запрашивает оба направления параллельно и объединяет результаты через RRF.
/// </summary>
public sealed class StepBackRetriever : IRetriever
{
    private readonly IRetriever _inner;
    private readonly IChatClient _chat;
    private readonly RrfMerger _merger;
    private readonly string _promptTemplate;

    public const string DefaultPromptTemplate =
        "Сформулируйте более широкий, концептуальный вопрос высокого уровня, " +
        "который поможет найти базовую теорию или контекст для следующего частного вопроса. " +
        "Ответьте ТОЛЬКО одной строкой вопроса без кавычек и пояснений.\n\nВопрос: {query}";

    public StepBackRetriever(
        IRetriever inner,
        IChatClient chat,
        RrfMerger? merger = null,
        string? promptTemplate = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));
        _merger = merger ?? new RrfMerger();
        _promptTemplate = promptTemplate ?? DefaultPromptTemplate;
    }

    public string Strategy => $"step-back({_inner.Strategy})";

    public async Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        string query,
        int topK,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);

        var prompt = _promptTemplate.Replace("{query}", query, StringComparison.Ordinal);
        string? stepBack = null;

        try
        {
            var response = await _chat.GetResponseAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);
            stepBack = response.Text?.Trim();
        }
        catch (Exception)
        {
            // Fallback: при сбое LLM используем только исходный запрос
        }

        var candidateK = Math.Max(topK * 2, topK + 5);
        var originalTask = _inner.RetrieveAsync(query, candidateK, cancellationToken);

        if (string.IsNullOrWhiteSpace(stepBack) || string.Equals(stepBack, query, StringComparison.OrdinalIgnoreCase))
        {
            var originalResults = await originalTask.ConfigureAwait(false);
            return originalResults.Take(topK).ToList();
        }

        var broaderTask = _inner.RetrieveAsync(stepBack, candidateK, cancellationToken);

        await Task.WhenAll(originalTask, broaderTask).WaitAsync(cancellationToken).ConfigureAwait(false);

        return _merger.Merge(originalTask.Result, broaderTask.Result, topK);
    }
}
