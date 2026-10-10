using System.Security.Cryptography;
using System.Text;
using Quarry.Core.Documents;

namespace Quarry.Security.Abstractions
{
    /// <summary>
    /// Produces a tamper-evident provenance signature for a <see cref="DocumentChunk"/>
    /// at ingest time.
    /// </summary>
    public interface IProvenanceSigner
    {
        /// <summary>Returns the provenance signature for <paramref name="chunk"/>.</summary>
        string Sign(DocumentChunk chunk);
    }

    /// <summary>
    /// A single security event worth surfacing to operators or a SIEM.
    /// </summary>
    public sealed record SecurityIncident(string Kind, string Detail, DateTimeOffset DetectedAtUtc);

    /// <summary>
    /// Sink for <see cref="SecurityIncident"/> notifications.
    /// </summary>
    public interface ISecurityAlertSink
    {
        /// <summary>Raises <paramref name="incident"/> to the configured destination.</summary>
        Task RaiseAsync(SecurityIncident incident, CancellationToken cancellationToken = default);
    }
}

namespace Quarry.Security
{
    using Quarry.Security.Abstractions;

    /// <summary>
    /// Signs chunks with an HMAC-SHA256 over <c>"{ChunkId}|{DocumentId}|{Text}"</c>,
    /// returning the signature as lowercase hex.
    /// </summary>
    public sealed class HmacProvenanceSigner : IProvenanceSigner
    {
        private readonly byte[] _key;

        public HmacProvenanceSigner(ReadOnlyMemory<byte> key)
        {
            if (key.IsEmpty)
            {
                throw new ArgumentException("Signing key must be non-empty.", nameof(key));
            }
            _key = key.ToArray();
        }

        public HmacProvenanceSigner(byte[] key)
            : this((key ?? throw new ArgumentNullException(nameof(key))).AsMemory())
        {
        }

        public string Sign(DocumentChunk chunk)
        {
            ArgumentNullException.ThrowIfNull(chunk);
            var payload = Encoding.UTF8.GetBytes($"{chunk.ChunkId}|{chunk.DocumentId}|{chunk.Text}");
            var mac = HMACSHA256.HashData(_key, payload);
            return Convert.ToHexStringLower(mac);
        }
    }
}

namespace Quarry.Security.Input
{
    /// <summary>
    /// Per-principal sliding-window rate limiter keyed on <c>(tenantId, userId)</c>.
    /// </summary>
    public sealed class RateLimiter
    {
        private readonly int _maxRequests;
        private readonly TimeSpan _window;
        private readonly TimeProvider _timeProvider;
        private readonly Lock _gate = new();
        private readonly Dictionary<(string Tenant, string User), Queue<long>> _hits = [];

        /// <summary>Maximum requests permitted per principal per window.</summary>
        public int MaxRequests => _maxRequests;

        /// <summary>Length of the sliding window.</summary>
        public TimeSpan Window => _window;

        public RateLimiter(int maxRequests, TimeSpan window, TimeProvider? timeProvider = null)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRequests);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);
            _maxRequests = maxRequests;
            _window = window;
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// Attempts to admit one request for <c>(tenantId, userId)</c>. Returns true
        /// and records the hit when under the limit; returns false when the window is full.
        /// </summary>
        public bool TryAcquire(string tenantId, string userId)
        {
            ArgumentException.ThrowIfNullOrEmpty(tenantId);
            ArgumentException.ThrowIfNullOrEmpty(userId);

            var nowTicks = _timeProvider.GetUtcNow().UtcTicks;
            var cutoff = nowTicks - _window.Ticks;
            var key = (tenantId, userId);

            lock (_gate)
            {
                if (!_hits.TryGetValue(key, out var timestamps))
                {
                    timestamps = new Queue<long>();
                    _hits[key] = timestamps;
                }

                while (timestamps.Count > 0 && timestamps.Peek() <= cutoff)
                {
                    timestamps.Dequeue();
                }

                if (timestamps.Count >= _maxRequests)
                {
                    return false;
                }

                timestamps.Enqueue(nowTicks);
                return true;
            }
        }
    }
}
