namespace Quarry.Ingestion.Events;

using Quarry.Core.Documents;

/// <summary>
/// Тип события изменения документа.
/// </summary>
public enum DocumentChangeType
{
    Created,
    Updated,
    Deleted
}

/// <summary>
/// Событие изменения документа в источнике данных.
/// </summary>
public sealed record DocumentChanged(
    string DocumentId,
    DocumentChangeType ChangeType,
    DateTimeOffset Timestamp,
    Document? Document = null);
