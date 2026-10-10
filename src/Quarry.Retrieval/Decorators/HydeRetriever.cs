namespace Quarry.Retrieval.Decorators;

using Microsoft.Extensions.AI;
using Quarry.Core.Abstractions;
using Quarry.Core.Documents;

/// <summary>
/// Декоратор Hypothetical Document Embeddings (HyDE, Gao et al. 2022).
/// Генерирует гипотетический фрагмент ответа через LLM, векторизует его и находит реальные документы,
/// сближая пространство запросов и документов в векторном пространстве.
/// </summary>
public sealed class HydeRetriever : IRetriever
{
    private readonly IRetriever _inner;
    private readonly IChatClient _chat;
    private readonly string _promptTemplate;

    public const string DefaultPromptTemplate =
        "Напишите короткий гипотетический абзац, который прямо и полно отвечает на вопрос пользователя. " +
        "Не добавляйте вводных фраз или пояснений.\n\nВопрос: {query}";

    public HydeRetriever(IRetriever inner, IChatClient chat, string? promptTemplate = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));
        _promptTemplate = promptTemplate ?? DefaultPromptTemplate;
    }

    public string Strategy => $"hyde({_inner.Strategy})";

    public async Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        string query,
        int topK,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);

        var prompt = _promptTemplate.Replace("{query}", query, StringComparison.Ordinal);

        try
        {
            var response = await _chat.GetResponseAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);
            var passage = response.Text?.Trim();

            if (!string.IsNullOrWhiteSpace(passage))
            {
                return await _inner.RetrieveAsync(passage, topK, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // При ошибке LLM происходит плавный fallback на исходный запрос
        }

        return await _inner.RetrieveAsync(query, topK, cancellationToken).ConfigureAwait(false);
    }
}
