namespace Quarry.Core.Configuration;

/// <summary>
/// Umbrella options object bound to the top-level <c>Quarry</c> configuration
/// section. Groups the cross-cutting Quarry settings so a single
/// <c>IOptions&lt;QuarryOptions&gt;</c> resolve exposes every sub-section.
/// </summary>
public sealed class QuarryOptions
{
    /// <summary>Configuration section path bound to this class.</summary>
    public const string SectionName = "Quarry";

    /// <summary>Ingestion-time chunking settings.</summary>
    public IngestionOptions Ingestion { get; set; } = new();

    /// <summary>Reranking settings — mode, candidate pool, and relevance floor.</summary>
    public RerankingOptions Reranking { get; set; } = new();
}
