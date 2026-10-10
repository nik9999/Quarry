namespace Quarry.Ingestion.Events;

/// <summary>
/// Поток событий изменения документов (Change Feed).
/// </summary>
public interface IChangeFeed
{
    /// <summary>
    /// Публикует событие изменения документа в поток.
    /// </summary>
    Task PublishChangeAsync(DocumentChanged change, CancellationToken cancellationToken = default);

    /// <summary>
    /// Непрерывно читает поток изменений документов.
    /// </summary>
    IAsyncEnumerable<DocumentChanged> ReadChangesAsync(CancellationToken cancellationToken = default);
}
