namespace Quarry.Retrieval.Vectorless;

using System.Text.RegularExpressions;

/// <summary>
/// Синтаксический анализатор структуры Markdown-документов: преобразует иерархию заголовков (#, ##, ###)
/// и перекрестные ссылки в дерево <see cref="StructuralIndex"/>.
/// </summary>
public sealed partial class DocumentStructureParser
{
    [GeneratedRegex(@"^(#{1,6})\s+(.+)$", RegexOptions.Compiled)]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"(?:\[(?:см\.|see|cf\.)\s+([^\]]+)\]|§\s*[\d\.]+|Art\s*[\d\.]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex CrossRefRegex();

    public static StructuralIndex Parse(string documentId, string markdownText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentNullException.ThrowIfNull(markdownText);

        var lines = markdownText.Split('\n');
        var builders = new List<NodeBuilder>();

        // Создаем корневой узел документа
        var rootBuilder = new NodeBuilder("root", documentId, "Документ", 0, null);
        builders.Add(rootBuilder);

        var stack = new Stack<NodeBuilder>();
        stack.Push(rootBuilder);

        var currentBodyLines = new List<string>();
        NodeBuilder currentBuilder = rootBuilder;
        var nodeCounter = 1;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            var match = HeadingRegex().Match(line);

            if (match.Success)
            {
                // Завершаем сборку тела предыдущего узла
                currentBuilder.Body = string.Join("\n", currentBodyLines).Trim();
                currentBodyLines.Clear();

                var level = match.Groups[1].Value.Length;
                var rawTitle = match.Groups[2].Value.Trim();

                // Извлекаем идентификатор раздела, если он присутствует в заголовке
                var nodeId = ExtractSectionId(rawTitle) ?? $"sec_{nodeCounter++}";

                // Ищем подходящего родителя в стеке по уровню заголовка
                while (stack.Count > 1 && stack.Peek().Level >= level)
                {
                    stack.Pop();
                }

                var parent = stack.Peek();
                var path = parent.Level == 0 ? rawTitle : $"{parent.Path} > {rawTitle}";

                var newBuilder = new NodeBuilder(nodeId, path, rawTitle, level, parent.Id);
                parent.ChildIds.Add(nodeId);
                builders.Add(newBuilder);

                stack.Push(newBuilder);
                currentBuilder = newBuilder;
            }
            else
            {
                currentBodyLines.Add(line);
            }
        }

        currentBuilder.Body = string.Join("\n", currentBodyLines).Trim();

        // Сборка финальных узлов и извлечение перекрестных ссылок
        var nodes = new List<StructuralNode>(builders.Count);
        foreach (var b in builders)
        {
            var crossRefs = ExtractCrossReferences(b.Body);
            nodes.Add(new StructuralNode(
                b.Id,
                b.Path,
                b.Title,
                b.Level,
                b.ParentId,
                b.ChildIds,
                b.Body,
                crossRefs));
        }

        return new StructuralIndex(nodes[0], nodes);
    }

    private static string? ExtractSectionId(string title)
    {
        var match = Regex.Match(title, @"^(§\s*[\d\.]+|Art(?:icle)?\s*[\d\.]+|Chapter\s*\d+|Раздел\s*\d+|Глава\s*\d+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Value.Trim().TrimEnd('.').Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();
        }

        return null;
    }

    private static List<string> ExtractCrossReferences(string body)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(body))
        {
            return results;
        }

        var matches = CrossRefRegex().Matches(body);
        foreach (Match m in matches)
        {
            var val = m.Groups[1].Success ? m.Groups[1].Value : m.Value;
            var clean = val.Trim().TrimEnd('.').Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();
            if (!results.Contains(clean, StringComparer.OrdinalIgnoreCase))
            {
                results.Add(clean);
            }
        }

        return results;
    }

    private sealed class NodeBuilder
    {
        public string Id { get; }
        public string Path { get; }
        public string Title { get; }
        public int Level { get; }
        public string? ParentId { get; }
        public string Body { get; set; } = string.Empty;
        public List<string> ChildIds { get; } = [];

        public NodeBuilder(string id, string path, string title, int level, string? parentId)
        {
            Id = id;
            Path = path;
            Title = title;
            Level = level;
            ParentId = parentId;
        }
    }
}
