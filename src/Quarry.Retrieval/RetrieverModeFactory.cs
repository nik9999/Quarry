namespace Quarry.Retrieval;

using Quarry.Core.Abstractions;

/// <summary>
/// Фабрика выбора режима ретривера по строковому имени ("dense", "sparse", "hybrid").
/// </summary>
public static class RetrieverModeFactory
{
    public static readonly IReadOnlyList<string> SupportedModes = ["dense", "sparse", "hybrid"];

    /// <summary>
    /// Создает ретривер для указанного режима <paramref name="mode"/>.
    /// </summary>
    public static IRetriever Create(
        string mode,
        IRetriever dense,
        IRetriever sparse,
        RrfMerger? merger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mode);
        ArgumentNullException.ThrowIfNull(dense);
        ArgumentNullException.ThrowIfNull(sparse);

        return mode.Trim().ToLowerInvariant() switch
        {
            "dense" => dense,
            "sparse" => sparse,
            "hybrid" => new HybridRetriever(dense, sparse, merger),
            _ => throw new ArgumentException(
                $"Неизвестный режим ретривера '{mode}'. Поддерживаемые режимы: {string.Join(", ", SupportedModes)}.",
                nameof(mode))
        };
    }
}
