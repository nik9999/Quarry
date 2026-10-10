namespace Quarry.Ingestion.Indexing;

using Quarry.Core.Documents;

/// <summary>
/// Конвейер сквозной индексации документов: стратегия -> эмбеддинги -> векторное хранилище.
/// </summary>
public interface IIndexingPipeline
{
    /// <summary>
    /// Индексирует коллекцию документов.
    /// </summary>
    Task<IndexingResult> IngestAsync(IEnumerable<Document> documents, CancellationToken cancellationToken = default);

    /// <summary>
    /// Индексирует асинхронный поток документов.
    /// </summary>
    Task<IndexingResult> IngestAsync(IAsyncEnumerable<Document> documents, CancellationToken cancellationToken = default);
}
