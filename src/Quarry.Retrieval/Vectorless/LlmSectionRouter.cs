namespace Quarry.Retrieval.Vectorless;

using Microsoft.Extensions.AI;

/// <summary>
/// Маршрутизатор разделов на базе LLM (Vectorless Section Router):
/// анализирует оглавление документа и сопоставляет вопрос пользователя с наиболее подходящими разделами без векторного поиска.
/// </summary>
public sealed class LlmSectionRouter
{
    private readonly StructuralIndex _index;
    private readonly IChatClient _chat;

    public LlmSectionRouter(StructuralIndex index, IChatClient chat)
    {
        _index = index ?? throw new ArgumentNullException(nameof(index));
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));
    }

    /// <summary>
    /// Маршрутизирует вопрос пользователя к релевантным разделам документа по оглавлению.
    /// </summary>
    public async Task<IReadOnlyList<StructuralNode>> RouteAsync(
        string question,
        int maxSections = 3,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSections);

        var outlineLines = _index.AllNodes
            .Where(n => n.Level > 0)
            .Select(n => $"{n.Id}: {n.Path}");

        var outline = string.Join("\n", outlineLines);

        var prompt =
            $"Ниже приведено оглавление документа с идентификаторами разделов:\n\n{outline}\n\n" +
            $"Определите до {maxSections} наиболее релевантных идентификаторов разделов для ответа на вопрос пользователя. " +
            $"Верните ТОЛЬКО идентификаторы (по одному на строке, без лишнего текста):\n\nВопрос: {question}";

        try
        {
            var response = await _chat.GetResponseAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(response.Text))
            {
                var candidateIds = response.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var matched = new List<StructuralNode>();

                foreach (var cid in candidateIds)
                {
                    var cleanId = cid.Trim().Trim('-', '*', ' ', '#');
                    var node = _index.Lookup(cleanId);
                    if (node is not null && !matched.Contains(node))
                    {
                        matched.Add(node);
                    }
                }

                if (matched.Count > 0)
                {
                    return matched.Take(maxSections).ToList();
                }
            }
        }
        catch (Exception)
        {
            // Graceful fallback
        }

        return [];
    }
}
