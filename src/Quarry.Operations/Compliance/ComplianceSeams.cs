using System.Collections.Concurrent;
using Quarry.Core.Abstractions;
using Quarry.Core.Documents;
using Quarry.Generation.Citations;

namespace Quarry.Operations.Compliance;

/// <summary>
/// Read seam over the EU AI Act audit log.
/// </summary>
public interface IAuditRecordStore
{
    /// <summary>
    /// Resolve the audit row recorded for <paramref name="queryId"/>, or
    /// <see langword="null"/> when no such row exists.
    /// </summary>
    Task<AuditEntry?> GetAsync(string queryId, CancellationToken cancellationToken = default);
}

/// <summary>
/// In-memory <see cref="IAuditRecordStore"/> for samples and tests.
/// </summary>
public sealed class InMemoryAuditRecordStore : IAuditRecordStore
{
    private readonly ConcurrentDictionary<string, AuditEntry> _rows = new(StringComparer.Ordinal);

    /// <summary>Record (or overwrite) the audit row for <paramref name="queryId"/>.</summary>
    public void Put(string queryId, AuditEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryId);
        ArgumentNullException.ThrowIfNull(entry);
        _rows[queryId] = entry;
    }

    public Task<AuditEntry?> GetAsync(string queryId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryId);
        _rows.TryGetValue(queryId, out var entry);
        return Task.FromResult(entry);
    }
}

/// <summary>
/// By-id lookup over document metadata.
/// </summary>
public interface IDocumentMetadataStore
{
    /// <summary>
    /// Resolve the metadata for <paramref name="documentId"/>, or
    /// <see langword="null"/> when unknown.
    /// </summary>
    Task<DocumentMetadata?> GetAsync(string documentId, CancellationToken cancellationToken = default);
}

/// <summary>In-memory <see cref="IDocumentMetadataStore"/> for samples and tests.</summary>
public sealed class InMemoryDocumentMetadataStore : IDocumentMetadataStore
{
    private readonly ConcurrentDictionary<string, DocumentMetadata> _docs = new(StringComparer.Ordinal);

    /// <summary>Record (or overwrite) the metadata for its <see cref="DocumentMetadata.Id"/>.</summary>
    public void Put(DocumentMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        _docs[metadata.Id] = metadata;
    }

    public Task<DocumentMetadata?> GetAsync(string documentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        _docs.TryGetValue(documentId, out var meta);
        return Task.FromResult(meta);
    }
}

/// <summary>
/// Dictionary-backed <see cref="IChunkLookup"/> keyed by
/// <see cref="DocumentChunk.ChunkId"/>.
/// </summary>
public sealed class InMemoryChunkLookup : IChunkLookup
{
    private readonly ConcurrentDictionary<string, DocumentChunk> _byId;

    /// <summary>Create an empty lookup; add chunks with <see cref="Add"/>.</summary>
    public InMemoryChunkLookup()
    {
        _byId = new ConcurrentDictionary<string, DocumentChunk>(StringComparer.Ordinal);
    }

    /// <summary>Create a lookup pre-populated from <paramref name="chunks"/>.</summary>
    public InMemoryChunkLookup(IEnumerable<DocumentChunk> chunks)
        : this()
    {
        ArgumentNullException.ThrowIfNull(chunks);
        foreach (var chunk in chunks)
        {
            Add(chunk);
        }
    }

    /// <summary>Insert or replace a chunk in the lookup.</summary>
    public void Add(DocumentChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        _byId[chunk.ChunkId] = chunk;
    }

    /// <inheritdoc />
    public Task<DocumentChunk?> GetByIdAsync(string chunkId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chunkId);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_byId.TryGetValue(chunkId, out var chunk) ? chunk : null);
    }
}

/// <summary>
/// Maps a <see cref="DocumentMetadata"/> to the canonical source uri a citation
/// links back to.
/// </summary>
public static class SourceUriResolver
{
    /// <summary>Returns a stable <c>smartdocs://doc/{id}</c> uri for the document.</summary>
    public static string Resolve(DocumentMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return $"smartdocs://doc/{metadata.Id}";
    }
}
