using System.Collections.Concurrent;

namespace Quarry.Operations.Compliance;

/// <summary>
/// A single per-user, per-scope consent grant.
/// </summary>
public sealed record ConsentGrant(
    string UserId,
    string Scope,
    string Version,
    bool Granted,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Per-user, per-scope consent ledger.
/// </summary>
public sealed class ConsentRegistry
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ConsentGrant>> _grants =
        new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public ConsentRegistry(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Record (or update) a granted consent for <paramref name="userId"/> on <paramref name="scope"/>.</summary>
    public Task RecordAsync(string userId, string scope, string version, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        var scopes = _grants.GetOrAdd(userId, _ => new ConcurrentDictionary<string, ConsentGrant>(StringComparer.Ordinal));
        scopes[scope] = new ConsentGrant(userId, scope, version, Granted: true, _timeProvider.GetUtcNow());
        return Task.CompletedTask;
    }

    /// <summary>Current grants for a user across all scopes (granted and revoked).</summary>
    public Task<IReadOnlyList<ConsentGrant>> GetCurrentAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        IReadOnlyList<ConsentGrant> result = _grants.TryGetValue(userId, out var scopes)
            ? [.. scopes.Values.OrderBy(g => g.Scope, StringComparer.Ordinal)]
            : [];
        return Task.FromResult(result);
    }

    /// <summary>
    /// Revoke a single scope's consent for a user.
    /// </summary>
    public Task<bool> RevokeAsync(string userId, string scope, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        if (_grants.TryGetValue(userId, out var scopes) &&
            scopes.TryGetValue(scope, out var existing) &&
            existing.Granted)
        {
            scopes[scope] = existing with { Granted = false, UpdatedAt = _timeProvider.GetUtcNow() };
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    /// <summary>
    /// Coverage over a known user population for a given scope.
    /// </summary>
    public Task<ConsentCoverage> CoverageAsync(
        IReadOnlyCollection<string> population,
        string scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        var covered = population.Count(userId =>
            _grants.TryGetValue(userId, out var scopes) &&
            scopes.TryGetValue(scope, out var grant) &&
            grant.Granted);

        var fraction = population.Count == 0 ? 1.0 : (double)covered / population.Count;
        return Task.FromResult(new ConsentCoverage(scope, covered, population.Count, fraction));
    }
}

/// <summary>Consent coverage for one scope over a population.</summary>
public sealed record ConsentCoverage(string Scope, int Covered, int Total, double Fraction);
