namespace Quarry.Retrieval.Vectorless;

/// <summary>
/// Узел иерархической структуры документа (раздел, глава, подраздел или статья).
/// </summary>
public sealed record StructuralNode(
    string Id,
    string Path,
    string Title,
    int Level,
    string? ParentId,
    IReadOnlyList<string> ChildIds,
    string Text,
    IReadOnlyList<string> CrossReferences);
