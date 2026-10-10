using System.Diagnostics.Metrics;

namespace Quarry.Performance.Cost;

/// <summary>
/// Per-token pricing (USD) used to turn token usage into a dollar figure.
/// </summary>
public sealed record TokenPricing(
    double InputPerToken,
    double OutputPerToken,
    double EmbeddingPerToken)
{
    /// <summary>Default pricing configuration.</summary>
    public static TokenPricing Default { get; } = new(
        InputPerToken: 0.15 / 1_000_000,
        OutputPerToken: 0.60 / 1_000_000,
        EmbeddingPerToken: 0.02 / 1_000_000);
}

/// <summary>
/// Shared meter and cost counter for cost tracking.
/// </summary>
public static class CostMeter
{
    public const string MeterName = "Quarry.Cost";
    public const string TenantTag = "tenant";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<double> CostUsd =
        Meter.CreateCounter<double>("quarry.cost.usd", unit: "USD", description: "LLM and embedding spend in USD.");

    public static void RecordCost(double costUsd, string tenant)
    {
        if (costUsd <= 0)
        {
            return;
        }
        CostUsd.Add(costUsd, new KeyValuePair<string, object?>(TenantTag, tenant));
    }
}
