namespace Quarry.Core.Abstractions;

using Quarry.Core.Documents;

/// <summary>
/// Контракт стратегии разбиения (чанкинга) документов на фрагменты.
/// </summary>
public interface IChunker
{
    /// <summary>
    /// Название или идентификатор стратегии разбиения (например, "fixed", "sentence", "recursive", "token", "semantic", "code").
    /// </summary>
    string Strategy { get; }

    /// <summary>
    /// Асинхронно разбивает документ на поток фрагментов.
    /// </summary>
    /// <param name="document">Исходный документ.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Асинхронный поток чанков документа.</returns>
    IAsyncEnumerable<DocumentChunk> ChunkAsync(Document document, CancellationToken cancellationToken = default);
}
