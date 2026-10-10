namespace Quarry.Ingestion.Indexing;

using Quarry.Core.Abstractions;
using Quarry.Core.Documents;

/// <summary>
/// Элемент, подготовленный стратегией индексации для генерации эмбеддинга и сохранения в векторное хранилище.
/// </summary>
public sealed record IndexedItem(
    DocumentChunk Chunk,
    EmbeddingPrompt? Prompt = null,
    string? IndexingKey = null,
    IReadOnlyDictionary<string, string>? Metadata = null);
