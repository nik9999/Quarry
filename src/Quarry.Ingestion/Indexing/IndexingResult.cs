namespace Quarry.Ingestion.Indexing;

/// <summary>
/// Результат выполнения пакетной индексации документов в векторное хранилище.
/// </summary>
public sealed record IndexingResult(
    int TotalDocuments,
    int TotalChunksIndexed,
    TimeSpan Duration);
