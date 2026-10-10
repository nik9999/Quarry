namespace Quarry.Core.Abstractions;

/// <summary>
/// Управляет префиксами для асимметричных моделей эмбеддингов (например, e5, nomic, bge).
/// </summary>
public sealed class EmbeddingPrompt
{
    private readonly string _documentPrefix;
    private readonly string _queryPrefix;

    public EmbeddingPrompt(string documentPrefix = "", string queryPrefix = "")
    {
        _documentPrefix = documentPrefix;
        _queryPrefix = queryPrefix;
    }

    public static EmbeddingPrompt None { get; } = new(string.Empty, string.Empty);

    /// <summary>Префиксы для семейств моделей E5 (passage: / query:).</summary>
    public static EmbeddingPrompt E5 { get; } = new("passage: ", "query: ");

    /// <summary>Префиксы для моделей Nomic.</summary>
    public static EmbeddingPrompt Nomic { get; } = new("search_document: ", "search_query: ");

    /// <summary>Префиксы для моделей BGE.</summary>
    public static EmbeddingPrompt Bge { get; } = new(string.Empty, "Represent this sentence for searching relevant passages: ");

    public string Apply(string text, EmbeddingTaskType taskType)
    {
        var prefix = taskType switch
        {
            EmbeddingTaskType.Document => _documentPrefix,
            EmbeddingTaskType.Query => _queryPrefix,
            _ => string.Empty
        };

        return string.IsNullOrEmpty(prefix) ? text : prefix + text;
    }
}
