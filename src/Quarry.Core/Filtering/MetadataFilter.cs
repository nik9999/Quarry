namespace Quarry.Core.Filtering;

/// <summary>
/// Фильтр для предварительного отбора чанков по метаданным (Pre-filter) до вычисления дистанций.
/// </summary>
public sealed class MetadataFilter
{
    public Dictionary<string, string> ExactMatches { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public MetadataFilter() { }

    public MetadataFilter(string key, string value)
    {
        ExactMatches[key] = value;
    }

    public bool Matches(IReadOnlyDictionary<string, string>? metadata)
    {
        if (ExactMatches.Count == 0) return true;
        if (metadata == null) return false;

        foreach (var (key, expected) in ExactMatches)
        {
            string? actual = null;
            if (metadata.TryGetValue(key, out var val))
            {
                actual = val;
            }
            else
            {
                foreach (var (k, v) in metadata)
                {
                    if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                    {
                        actual = v;
                        break;
                    }
                }
            }

            if (actual == null || !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
