namespace Quarry.Ingestion.Indexing;

using Quarry.Core.Documents;

/// <summary>
/// Стратегия преобразования исходного документа в набор индексируемых элементов.
/// </summary>
public interface IIndexingStrategy
{
    /// <summary>
    /// Имя или идентификатор стратегии (например, "chunk", "sub-chunk", "hyde", "summary").
    /// </summary>
    string StrategyName { get; }

    /// <summary>
    /// Асинхронно обрабатывает документ и формирует поток элементов для индексации.
    /// </summary>
    IAsyncEnumerable<IndexedItem> ProcessAsync(Document document, CancellationToken cancellationToken = default);
}
