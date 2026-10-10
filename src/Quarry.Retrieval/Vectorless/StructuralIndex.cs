namespace Quarry.Retrieval.Vectorless;

/// <summary>
/// Индекс иерархической структуры документа с быстрым доступом O(1) по идентификаторам узлов.
/// </summary>
public sealed class StructuralIndex
{
    private readonly Dictionary<string, StructuralNode> _nodesById;

    public StructuralNode Root { get; }
    public IReadOnlyCollection<StructuralNode> AllNodes => _nodesById.Values;

    public StructuralIndex(StructuralNode root, IEnumerable<StructuralNode> nodes)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
        _nodesById = nodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
        if (!_nodesById.ContainsKey(root.Id))
        {
            _nodesById[root.Id] = root;
        }
    }

    /// <summary>
    /// Находит узел по его стабильному идентификатору за время O(1).
    /// </summary>
    public StructuralNode? Lookup(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return _nodesById.GetValueOrDefault(id);
    }
}
