using Quarry.Core.Documents;
using Quarry.Core.Filtering;

namespace Quarry.Core.Abstractions;

/// <summary>
/// Доменный порт хранилища векторных эмбеддингов.
/// </summary>
public interface IVectorStore
{
    /// <summary>Название коллекции / таблицы векторов.</summary>
    string CollectionName { get; }

    /// <summary>Идемпотентно проверяет и создает необходимые таблицы и индексы в базе данных.</summary>
    Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default);

    /// <summary>Вставляет или обновляет пачку векторизованных чанков.</summary>
    Task UpsertAsync(IEnumerable<EmbeddedChunk> chunks, CancellationToken cancellationToken = default);

    /// <summary>
    /// Выполняет поиск ближайших <paramref name="topK"/> чанков к вектору запроса <paramref name="queryVector"/>.
    /// </summary>
    /// <param name="queryVector">Вектор поискового запроса.</param>
    /// <param name="topK">Количество возвращаемых результатов.</param>
    /// <param name="filter">Опциональный фильтр метаданных, применяемый до ранжирования (Pre-filter).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<IReadOnlyList<RetrievalResult>> SearchAsync(
        ReadOnlyMemory<float> queryVector,
        int topK,
        MetadataFilter? filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>Удаляет чанки и их векторы по идентификаторам.</summary>
    Task DeleteAsync(IEnumerable<string> chunkIds, CancellationToken cancellationToken = default);
}
