using Quarry.Core.Documents;

namespace Quarry.Core.Abstractions;

/// <summary>
/// Доменный сервис создания векторных эмбеддингов для документов и поисковых запросов.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>Идентификатор или имя модели эмбеддингов.</summary>
    string EmbeddingModel { get; }

    /// <summary>Размерность выходного вектора (например, 384, 768, 1024).</summary>
    int Dimensions { get; }

    /// <summary>Создает эмбеддинг для отдельного чанка документа.</summary>
    Task<EmbeddedChunk> EmbedAsync(DocumentChunk chunk, CancellationToken cancellationToken = default);

    /// <summary>
    /// Создает эмбеддинг для пользовательского поискового запроса.
    /// Отдельный метод необходим, так как многие модели требуют специфического query-префикса.
    /// </summary>
    Task<ReadOnlyMemory<float>> EmbedQueryAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Потоковая генерация эмбеддингов для последовательности чанков.
    /// </summary>
    IAsyncEnumerable<EmbeddedChunk> EmbedAsync(
        IAsyncEnumerable<DocumentChunk> chunks,
        CancellationToken cancellationToken = default);
}
