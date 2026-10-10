using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using Quarry.Core.Documents;
using Quarry.Generation;
using Quarry.Performance.Cost;
using Quarry.Security.Input;

namespace Quarry.Operations.Budgeting;

/// <summary>
/// A pipeline-boundary budget governor. Sits in front of the real
/// <see cref="IRagPipeline"/> and refuses a tenant that is over its rolling spend
/// quota, over its request-rate ceiling, or inside a spend circuit-breaker
/// cool-down — throwing a <see cref="BudgetExceededException"/> before a single
/// token is spent.
/// </summary>
public sealed class BudgetEnforcingPipeline : IRagPipeline
{
    private readonly IRagPipeline _inner;
    private readonly BudgetPolicy _policy;
    private readonly TenantSpendLedger _ledger;
    private readonly RateLimiter _rateLimiter;
    private readonly Func<RagResponse, double> _costEstimator;
    private readonly Func<string> _tenantAccessor;
    private readonly Func<string> _userAccessor;
    private readonly TimeProvider _timeProvider;

    // tenant -> the UTC instant its spend breaker re-arms. Absent = closed.
    private readonly ConcurrentDictionary<string, DateTimeOffset> _breakerOpenUntil =
        new(StringComparer.Ordinal);

    /// <summary>Create the governor over an inner pipeline.</summary>
    /// <param name="inner">The real pipeline invoked once a request is admitted.</param>
    /// <param name="policy">The per-tenant budget policy (quota, rate, breaker cool-down).</param>
    /// <param name="ledger">The rolling spend ledger the quota reads and each answer's cost is recorded into.</param>
    /// <param name="tenantAccessor">
    /// Resolves the current tenant id. Defaults to a single <c>"default"</c> tenant when omitted.
    /// </param>
    /// <param name="pricing">
    /// Token pricing used by the default cost estimator. Defaults to <see cref="TokenPricing.Default"/>.
    /// </param>
    /// <param name="costEstimator">
    /// Prices a completed <see cref="RagResponse"/> in USD.
    /// </param>
    /// <param name="userAccessor">
    /// Resolves the current user id for the rate limiter's <c>(tenant, user)</c> key.
    /// </param>
    /// <param name="timeProvider">Clock for the breaker cool-down. Defaults to <see cref="TimeProvider.System"/>.</param>
    public BudgetEnforcingPipeline(
        IRagPipeline inner,
        BudgetPolicy policy,
        TenantSpendLedger ledger,
        Func<string>? tenantAccessor = null,
        TokenPricing? pricing = null,
        Func<RagResponse, double>? costEstimator = null,
        Func<string>? userAccessor = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(policy.SpendQuotaUsd);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(policy.MaxRequests);

        _inner = inner;
        _policy = policy;
        _ledger = ledger;
        _tenantAccessor = tenantAccessor ?? (static () => "default");
        _timeProvider = timeProvider ?? TimeProvider.System;

        var effectivePricing = pricing ?? TokenPricing.Default;
        _costEstimator = costEstimator ?? (response => EstimateCost(response, effectivePricing));

        _userAccessor = userAccessor ?? (static () => "all");
        _rateLimiter = new RateLimiter(policy.MaxRequests, policy.RateWindow, _timeProvider);
    }

    /// <inheritdoc />
    public async Task<RagResponse> AskAsync(string question, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        var tenant = _tenantAccessor();
        Admit(tenant);

        var response = await _inner.AskAsync(question, ct).ConfigureAwait(false);
        RecordSpend(tenant, response);
        return response;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<RagStreamEvent> AskStreamingAsync(
        string question,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        var tenant = _tenantAccessor();

        Admit(tenant);

        var answer = new StringBuilder();
        IReadOnlyList<RetrievalResult> sources = [];
        var faulted = false;

        await foreach (var ev in _inner.AskStreamingAsync(question, ct).ConfigureAwait(false))
        {
            switch (ev.Kind)
            {
                case RagStreamEventKind.Sources when ev.Sources is not null:
                    sources = ev.Sources;
                    break;
                case RagStreamEventKind.Token when ev.Token is not null:
                    answer.Append(ev.Token);
                    break;
                case RagStreamEventKind.Error:
                    faulted = true;
                    break;
                default:
                    break;
            }

            yield return ev;
        }

        if (!faulted)
        {
            RecordSpend(tenant, new RagResponse(answer.ToString(), sources, LatencyMs: 0, Strategy: "stream"));
        }
    }

    private void Admit(string tenant)
    {
        var now = _timeProvider.GetUtcNow();

        // 1. Spend circuit-breaker: if open and still cooling down, refuse fast.
        if (_breakerOpenUntil.TryGetValue(tenant, out var openUntil))
        {
            if (now < openUntil)
            {
                throw new BudgetExceededException(tenant, BudgetDenialReason.CircuitOpen);
            }

            _breakerOpenUntil.TryRemove(new KeyValuePair<string, DateTimeOffset>(tenant, openUntil));
        }

        // 2. Rate ceiling.
        if (!_rateLimiter.TryAcquire(tenant, _userAccessor()))
        {
            throw new BudgetExceededException(tenant, BudgetDenialReason.RateLimitExceeded);
        }

        // 3. Hard spend quota over the rolling window.
        if (_ledger.RollingSpend(tenant) >= _policy.SpendQuotaUsd)
        {
            _breakerOpenUntil[tenant] = now + _policy.BreakerCooldown;
            throw new BudgetExceededException(tenant, BudgetDenialReason.SpendQuotaExceeded);
        }
    }

    private void RecordSpend(string tenant, RagResponse response)
    {
        var cost = _costEstimator(response);
        if (cost <= 0)
        {
            return;
        }

        _ledger.Record(tenant, cost);
        CostMeter.RecordCost(cost, tenant);
    }

    private static double EstimateCost(RagResponse response, TokenPricing pricing)
    {
        var approxOutputTokens = (response.Answer?.Length ?? 0) / 4.0;
        return approxOutputTokens * pricing.OutputPerToken;
    }
}
