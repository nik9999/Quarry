using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Quarry.Operations.Compliance;

/// <summary>
/// One answer queued for human review.
/// </summary>
public sealed record ReviewItem(
    string ItemId,
    string QueryId,
    string Query,
    string Answer,
    double Confidence,
    string Reason,
    DateTimeOffset EnqueuedAt,
    string? ReviewerEdit = null,
    DateTimeOffset? ReviewedAt = null)
{
    /// <summary>Whether a reviewer has resolved this item.</summary>
    public bool IsResolved => ReviewedAt is not null;
}

/// <summary>
/// In-memory human-review queue.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "The 'Queue' suffix is the domain term for the human-review queue; the type genuinely is a queue.")]
public sealed class HumanReviewQueue
{
    private readonly ConcurrentQueue<string> _pending = new();
    private readonly ConcurrentDictionary<string, ReviewItem> _items = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public HumanReviewQueue(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Number of items still awaiting review.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>Enqueue an answer for review. Returns the created item.</summary>
    public ReviewItem Enqueue(string queryId, string query, string answer, double confidence, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var item = new ReviewItem(
            ItemId: Guid.NewGuid().ToString("n"),
            QueryId: queryId,
            Query: query,
            Answer: answer,
            Confidence: confidence,
            Reason: reason,
            EnqueuedAt: _timeProvider.GetUtcNow());
        _items[item.ItemId] = item;
        _pending.Enqueue(item.ItemId);
        return item;
    }

    /// <summary>
    /// Claim the oldest pending, unresolved item, or <see langword="null"/> when
    /// the queue is empty.
    /// </summary>
    public ReviewItem? Dequeue()
    {
        while (_pending.TryDequeue(out var id))
        {
            if (_items.TryGetValue(id, out var item) && !item.IsResolved)
            {
                return item;
            }
        }
        return null;
    }

    /// <summary>
    /// A reviewer overrides an item with their corrected answer.
    /// </summary>
    public ReviewItem? Override(string itemId, string reviewerEdit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentNullException.ThrowIfNull(reviewerEdit);

        if (!_items.TryGetValue(itemId, out var item))
        {
            return null;
        }

        var resolved = item with { ReviewerEdit = reviewerEdit, ReviewedAt = _timeProvider.GetUtcNow() };
        _items[itemId] = resolved;
        return resolved;
    }

    /// <summary>
    /// The resolved items as future eval cases.
    /// </summary>
    public IReadOnlyList<ReviewItem> ResolvedCases =>
        [.. _items.Values.Where(i => i.IsResolved).OrderBy(i => i.ReviewedAt)];
}
