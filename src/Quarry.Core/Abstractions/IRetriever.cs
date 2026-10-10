namespace Quarry.Core.Abstractions;

using Quarry.Core.Documents;

/// <summary>
/// Контракт поискового ретривера: преобразует произвольный текстовый запрос в ранжированный список результатов <see cref="RetrievalResult"/>.
/// </summary>
public interface IRetriever
{
    /// <summary>
    /// Уникальный стабильный идентификатор стратегии поиска (например, "dense", "sparse-bm25", "hybrid", "structural").
    /// </summary>
    string Strategy { get; }

    /// <summary>
    /// Асинхронно извлекает до <paramref name="topK"/> наиболее релевантных результатов для запроса <paramref name="query"/>.
    /// </summary>
    /// <param name="query">Текстовый поисковый запрос пользователя.</param>
    /// <param name="topK">Максимальное количество возвращаемых фрагментов.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Ранжированный список результатов поиска.</returns>
    Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        string query,
        int topK,
        CancellationToken cancellationToken = default);
}
