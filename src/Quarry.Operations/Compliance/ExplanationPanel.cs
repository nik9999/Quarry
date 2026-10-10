using System.Globalization;
using Quarry.Generation.Citations;

namespace Quarry.Operations.Compliance;

/// <summary>
/// EU AI Act risk tier for a given interaction.
/// </summary>
public enum RiskClassification
{
    Minimal,
    Limited,
    High,
}

/// <summary>One compact "chip" shown next to a limited-risk answer.</summary>
public sealed record ExplanationChip(string Label, string Value);

/// <summary>
/// The rendered explanation surface for an answer.
/// </summary>
public sealed record ExplanationView(
    RiskClassification Risk,
    bool ShowFullPanel,
    IReadOnlyList<ExplanationChip> Chips,
    IReadOnlyList<Citation> CitedChunks,
    IReadOnlyList<string> ReasoningChain,
    string? ModelName,
    string? ModelVersion,
    string? RequestHumanReviewAction);

/// <summary>The inputs a panel is built from.</summary>
public sealed record ExplanationContext(
    string Answer,
    double Confidence,
    IReadOnlyList<Citation> Citations,
    IReadOnlyList<string> ReasoningChain,
    string ModelName,
    string ModelVersion);

/// <summary>
/// Classifies an interaction into a <see cref="RiskClassification"/> tier.
/// </summary>
public interface IRiskClassifier
{
    RiskClassification Classify(ExplanationContext context);
}

/// <summary>Risk classifier that always returns a fixed tier.</summary>
public sealed class FixedRiskClassifier : IRiskClassifier
{
    private readonly RiskClassification _risk;

    public FixedRiskClassifier(RiskClassification risk) => _risk = risk;

    public RiskClassification Classify(ExplanationContext context) => _risk;
}

/// <summary>
/// Builds the explanation surface for an answer, gated on an injected
/// <see cref="IRiskClassifier"/>.
/// </summary>
public sealed class ExplanationPanel
{
    /// <summary>The action key the high-risk panel binds its review button to.</summary>
    public const string RequestHumanReviewActionKey = "request-human-review";

    private readonly IRiskClassifier _classifier;

    public ExplanationPanel(IRiskClassifier classifier)
    {
        ArgumentNullException.ThrowIfNull(classifier);
        _classifier = classifier;
    }

    /// <summary>Render the explanation view for <paramref name="context"/>, classifying its risk tier.</summary>
    public ExplanationView Render(ExplanationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var risk = _classifier.Classify(context);

        var chips = new List<ExplanationChip>
        {
            new("Confidence", context.Confidence.ToString("0.##", CultureInfo.InvariantCulture)),
            new("Sources", context.Citations.Count.ToString(CultureInfo.InvariantCulture)),
            new("Model", context.ModelName),
        };

        if (risk != RiskClassification.High)
        {
            return new ExplanationView(
                Risk: risk,
                ShowFullPanel: false,
                Chips: chips,
                CitedChunks: [],
                ReasoningChain: [],
                ModelName: null,
                ModelVersion: null,
                RequestHumanReviewAction: null);
        }

        return new ExplanationView(
            Risk: risk,
            ShowFullPanel: true,
            Chips: chips,
            CitedChunks: context.Citations,
            ReasoningChain: context.ReasoningChain,
            ModelName: context.ModelName,
            ModelVersion: context.ModelVersion,
            RequestHumanReviewAction: RequestHumanReviewActionKey);
    }
}
