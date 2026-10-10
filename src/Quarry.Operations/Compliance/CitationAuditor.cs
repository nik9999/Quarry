using Quarry.Core.Abstractions;
using Quarry.Generation.Citations;
using Quarry.Security.Abstractions;

namespace Quarry.Operations.Compliance;

/// <summary>
/// One link in the audit chain: a citation the answer made, resolved back to the
/// exact chunk it pointed at, with that chunk's provenance signature recomputed
/// and verified.
/// </summary>
public sealed record ChainStep(
    int CitationN,
    string ChunkId,
    string ChunkText,
    string DocumentId,
    string SourceUri,
    DateTimeOffset ModifiedAt,
    string ProvenanceSignature,
    bool SignatureValid);

/// <summary>
/// The full trace for one historical answer: the query id, the
/// answer text, and one <see cref="ChainStep"/> per citation, each carrying a
/// verified provenance signature.
/// </summary>
public sealed record AuditTrace(
    string QueryId,
    string Answer,
    IReadOnlyList<ChainStep> Chain);

/// <summary>
/// Traces a historical answer back through its citation chain to the source
/// chunks and proves each link.
/// </summary>
public sealed class CitationAuditor
{
    private readonly IAuditRecordStore _audit;
    private readonly IChunkLookup _chunks;
    private readonly IDocumentMetadataStore _documents;
    private readonly IProvenanceSigner _signer;

    public CitationAuditor(
        IAuditRecordStore audit,
        IChunkLookup chunks,
        IDocumentMetadataStore documents,
        IProvenanceSigner signer)
    {
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(signer);
        _audit = audit;
        _chunks = chunks;
        _documents = documents;
        _signer = signer;
    }

    /// <summary>
    /// Build the <see cref="AuditTrace"/> for the recorded answer to
    /// <paramref name="queryId"/>.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No audit row exists for the query id.</exception>
    public async Task<AuditTrace> TraceAsync(string queryId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryId);

        var entry = await _audit.GetAsync(queryId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"No audit record for query id '{queryId}'.");

        var steps = new List<ChainStep>(entry.Citations.Count);
        foreach (var citation in entry.Citations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            steps.Add(await BuildStepAsync(citation, cancellationToken).ConfigureAwait(false));
        }

        return new AuditTrace(queryId, entry.Response, steps);
    }

    private async Task<ChainStep> BuildStepAsync(Citation citation, CancellationToken cancellationToken)
    {
        var chunk = await _chunks.GetByIdAsync(citation.ChunkId, cancellationToken).ConfigureAwait(false);
        if (chunk is null)
        {
            return new ChainStep(
                CitationN: citation.SourceIndex,
                ChunkId: citation.ChunkId,
                ChunkText: string.Empty,
                DocumentId: citation.DocumentId,
                SourceUri: $"smartdocs://doc/{citation.DocumentId}",
                ModifiedAt: default,
                ProvenanceSignature: string.Empty,
                SignatureValid: false);
        }

        var doc = await _documents.GetAsync(chunk.DocumentId, cancellationToken).ConfigureAwait(false)
            ?? chunk.DocMetadata;
        var sourceUri = doc is not null ? SourceUriResolver.Resolve(doc) : $"smartdocs://doc/{chunk.DocumentId}";
        var modifiedAt = doc is not null ? new DateTimeOffset(doc.LastModified.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : default;

        var recomputed = _signer.Sign(chunk);
        var valid = chunk.Provenance is { } attached
            && CryptographicEquals(attached, recomputed);

        return new ChainStep(
            CitationN: citation.SourceIndex,
            ChunkId: chunk.ChunkId,
            ChunkText: chunk.Text,
            DocumentId: chunk.DocumentId,
            SourceUri: sourceUri,
            ModifiedAt: modifiedAt,
            ProvenanceSignature: recomputed,
            SignatureValid: valid);
    }

    private static bool CryptographicEquals(string a, string b) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a),
            System.Text.Encoding.UTF8.GetBytes(b));
}
