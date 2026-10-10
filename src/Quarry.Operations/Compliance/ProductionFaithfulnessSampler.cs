using Quarry.Core.Documents;
using Quarry.Evaluation;

namespace Quarry.Operations.Compliance;

/// <summary>
/// One production answer eligible for offline faithfulness re-scoring.
/// </summary>
public sealed record ProductionAnswer(
    string QueryId,
    string Query,
    string Answer,
    IReadOnlyList<RetrievalResult> Sources);

/// <summary>
/// A faithfulness judge over a single production answer. Wraps the
/// <see cref="GenerationEvaluator"/> behind a one-call seam so the
/// sampler can be driven by a deterministic stub offline.
/// </summary>
public interface IFaithfulnessJudge
{
    Task<double> ScoreAsync(ProductionAnswer answer, CancellationToken cancellationToken = default);
}

/// <summary>
/// Adapts the <see cref="GenerationEvaluator"/> to <see cref="IFaithfulnessJudge"/>.
/// </summary>
public sealed class GenerationEvaluatorJudge : IFaithfulnessJudge
{
    private readonly GenerationEvaluator _evaluator;

    public GenerationEvaluatorJudge(GenerationEvaluator evaluator)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        _evaluator = evaluator;
    }

    public async Task<double> ScoreAsync(ProductionAnswer answer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(answer);
        var assessment = await _evaluator
            .AssessAsync(answer.Query, answer.Answer, answer.Sources, cancellationToken)
            .ConfigureAwait(false);
        return assessment.FaithfulnessScore;
    }
}

/// <summary>A single recorded faithfulness sample.</summary>
public sealed record FaithfulnessSample(string QueryId, double Score, DateTimeOffset SampledAt);

/// <summary>Raised when the rolling 7-day faithfulness average drops below threshold.</summary>
public sealed record FaithfulnessAlert(double RollingAverage, double Threshold, int WindowSampleCount, DateTimeOffset RaisedAt);

/// <summary>Sink the sampler calls when the rolling average crosses the threshold.</summary>
public interface IFaithfulnessAlertSink
{
    Task AlertAsync(FaithfulnessAlert alert, CancellationToken cancellationToken = default);
}

/// <summary>Tunable knobs for the sampler.</summary>
/// <param name="SampleRate">Fraction of production answers to judge (default: 0.005 = 0.5%).</param>
/// <param name="Threshold">Rolling-average floor below which an alert fires (default: 0.82).</param>
/// <param name="WindowDays">Rolling-window length in days (default: 7).</param>
public sealed record FaithfulnessSamplerOptions(double SampleRate = 0.005, double Threshold = 0.82, int WindowDays = 7);

/// <summary>
/// Runs the LLM-as-judge faithfulness scorer over a small daily sample of
/// production answers, records each score, and alerts when the rolling 7-day
/// average falls below a configurable threshold (default 0.82).
/// </summary>
public sealed class ProductionFaithfulnessSampler
{
    private readonly IFaithfulnessJudge _judge;
    private readonly IFaithfulnessAlertSink _alerts;
    private readonly FaithfulnessSamplerOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly Func<double> _nextSampleRoll;
    private readonly List<FaithfulnessSample> _samples = [];
    private readonly Lock _gate = new();

    public ProductionFaithfulnessSampler(
        IFaithfulnessJudge judge,
        IFaithfulnessAlertSink alerts,
        FaithfulnessSamplerOptions? options = null,
        TimeProvider? timeProvider = null,
        Func<double>? nextSampleRoll = null)
    {
        ArgumentNullException.ThrowIfNull(judge);
        ArgumentNullException.ThrowIfNull(alerts);
        _judge = judge;
        _alerts = alerts;
        _options = options ?? new FaithfulnessSamplerOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
        var rng = Random.Shared;
        _nextSampleRoll = nextSampleRoll ?? (() => rng.NextDouble());
    }

    /// <summary>All recorded samples, oldest first.</summary>
    public IReadOnlyList<FaithfulnessSample> Samples
    {
        get
        {
            lock (_gate)
            {
                return [.. _samples];
            }
        }
    }

    /// <summary>
    /// Offer <paramref name="answer"/> to the sampler. With probability
    /// <see cref="FaithfulnessSamplerOptions.SampleRate"/> it is judged and
    /// recorded; otherwise it is skipped. Returns the recorded sample, or
    /// <see langword="null"/> when the answer was not selected.
    /// </summary>
    public async Task<FaithfulnessSample?> OfferAsync(ProductionAnswer answer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(answer);

        if (_nextSampleRoll() >= _options.SampleRate)
        {
            return null;
        }

        var score = await _judge.ScoreAsync(answer, cancellationToken).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow();
        var sample = new FaithfulnessSample(answer.QueryId, score, now);

        double rollingAverage;
        int windowCount;
        lock (_gate)
        {
            _samples.Add(sample);
            var cutoff = now - TimeSpan.FromDays(_options.WindowDays);
            var window = _samples.Where(s => s.SampledAt > cutoff).ToList();
            windowCount = window.Count;
            rollingAverage = windowCount == 0 ? 1.0 : window.Average(s => s.Score);
        }

        if (rollingAverage < _options.Threshold)
        {
            await _alerts.AlertAsync(
                new FaithfulnessAlert(rollingAverage, _options.Threshold, windowCount, now),
                cancellationToken).ConfigureAwait(false);
        }

        return sample;
    }
}
