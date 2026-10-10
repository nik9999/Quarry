namespace Quarry.Core.Documents;

/// <summary>
/// Представляет исходный текстовый документ до разбиения на чанки.
/// </summary>
public sealed record Document(
    string Id,
    string Text,
    IReadOnlyDictionary<string, string>? Metadata = null);
