namespace Quarry.Retrieval.Vectorless;

using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Quarry.Core.Abstractions;
using Quarry.Core.Documents;

/// <summary>
/// Структурный (безвекторный) ретривер: осуществляет поиск и навигацию по иерархическому дереву документа.
/// Позволяет находить статьи и пункты по их точным номерам (например, "§ 1.2", "Art 17", "Глава 3"),
/// раскрывать контекст предков/потомков и переходить по перекрестным ссылкам (Cross-References).
/// </summary>
public sealed partial class StructuralRetriever : IRetriever
{
    private readonly StructuralIndex _index;
    private readonly IChatClient? _chat;
    private readonly IRetriever? _topicFallback;

    public string Strategy => "structural";

    [GeneratedRegex(@"(§\s*[\d\.]+|Art(?:icle)?\s*[\d\.]+|Chapter\s*\d+|Раздел\s*\d+|Глава\s*\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ExplicitIdRegex();

    public StructuralRetriever(
        StructuralIndex index,
        IChatClient? chat = null,
        IRetriever? topicFallback = null)
    {
        _index = index ?? throw new ArgumentNullException(nameof(index));
        _chat = chat;
        _topicFallback = topicFallback;
    }

    /// <summary>
    /// Находит узел по его стабильному идентификатору за O(1).
    /// </summary>
    public StructuralNode? Lookup(string id) => _index.Lookup(id);

    /// <summary>
    /// Возвращает потомков узла в порядке обхода в ширину (BFS).
    /// </summary>
    public IReadOnlyList<StructuralNode> Descendants(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var results = new List<StructuralNode>();
        var queue = new Queue<string>();

        if (Lookup(id) is { } start)
        {
            foreach (var childId in start.ChildIds)
            {
                queue.Enqueue(childId);
            }
        }

        while (queue.TryDequeue(out var currentId))
        {
            if (Lookup(currentId) is not { } node)
            {
                continue;
            }

            results.Add(node);
            foreach (var childId in node.ChildIds)
            {
                queue.Enqueue(childId);
            }
        }

        return results;
    }

    /// <summary>
    /// Возвращает цепочку предков узла от ближайшего родителя до корня.
    /// </summary>
    public IReadOnlyList<StructuralNode> Ancestors(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var results = new List<StructuralNode>();
        var current = Lookup(id)?.ParentId;

        while (current is not null && Lookup(current) is { } node)
        {
            results.Add(node);
            current = node.ParentId;
        }

        return results;
    }

    /// <summary>
    /// Возвращает соседние узлы того же уровня (дети общего родителя, исключая сам узел).
    /// </summary>
    public IReadOnlyList<StructuralNode> Siblings(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (Lookup(id) is not { ParentId: { } parentId } || Lookup(parentId) is not { } parent)
        {
            return [];
        }

        return parent.ChildIds
            .Where(childId => !string.Equals(childId, id, StringComparison.OrdinalIgnoreCase))
            .Select(Lookup)
            .Where(n => n is not null)
            .Select(n => n!)
            .ToList();
    }

    /// <summary>
    /// Замыкание узла и связанных с ним узлов по перекрестным ссылкам (BFS до указанной глубины).
    /// </summary>
    public IReadOnlyList<StructuralNode> WithCrossReferences(string id, int depth = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (depth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(depth), "Depth must be non-negative.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { id };
        var queue = new Queue<(string Id, int Depth)>();
        queue.Enqueue((id, 0));
        var results = new List<StructuralNode>();

        while (queue.TryDequeue(out var item))
        {
            var (currentId, currentDepth) = item;
            if (Lookup(currentId) is not { } node)
            {
                continue;
            }

            results.Add(node);

            if (currentDepth < depth)
            {
                foreach (var refId in node.CrossReferences)
                {
                    if (seen.Add(refId))
                    {
                        queue.Enqueue((refId, currentDepth + 1));
                    }
                }
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        string query,
        int topK,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);

        // 1. Попытка извлечь явный идентификатор (регулярное выражение или LLM)
        var explicitId = await ExtractIdentifierAsync(query, cancellationToken).ConfigureAwait(false);

        if (explicitId is not null && WithCrossReferences(explicitId, depth: 1) is { Count: > 0 } nodes)
        {
            return ToResults(nodes, topK, _index.Root.Title);
        }

        // 2. Если точный раздел не назван — используем фоллбэк ретривер
        if (_topicFallback is not null)
        {
            return await _topicFallback.RetrieveAsync(query, topK, cancellationToken).ConfigureAwait(false);
        }

        return [];
    }

    private async Task<string?> ExtractIdentifierAsync(string query, CancellationToken cancellationToken)
    {
        // Быстрая проверка регулярным выражением
        var match = ExplicitIdRegex().Match(query);
        if (match.Success)
        {
            var clean = match.Value.Trim().TrimEnd('.').Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();
            if (Lookup(clean) is not null)
            {
                return clean;
            }
        }

        // Если есть подключение к LLM, запрашиваем извлечение идентификатора
        if (_chat is not null)
        {
            try
            {
                var prompt =
                    "Определите, упоминается ли в вопросе конкретный номер раздела/статьи/пункта (например, \"§ 1.2\", \"Art 17\", \"Глава 3\"). " +
                    "Если да, верните ТОЛЬКО этот идентификатор. Если нет, верните пустую строку.\n\n" +
                    $"Вопрос: {query}";

                var response = await _chat.GetResponseAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);
                var text = response.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var clean = text.Trim().TrimEnd('.').Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();
                    if (Lookup(clean) is not null)
                    {
                        return clean;
                    }
                }
            }
            catch (Exception)
            {
                // Fallback
            }
        }

        return null;
    }

    private static List<RetrievalResult> ToResults(
        IReadOnlyList<StructuralNode> nodes,
        int topK,
        string documentTitle)
    {
        return nodes.Take(topK).Select((n, rank) =>
        {
            var text = $"{n.Path}\n\n{n.Text}";
            var meta = new Dictionary<string, string>
            {
                ["structural_id"] = n.Id,
                ["structural_path"] = n.Path,
                ["document_title"] = documentTitle
            };

            var chunk = new DocumentChunk(n.Id, documentTitle, rank, text, meta, 0, text.Length);
            return new RetrievalResult(chunk, 1.0 / (rank + 1));
        }).ToList();
    }
}
