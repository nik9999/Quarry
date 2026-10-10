namespace Quarry.Ingestion.Chunking;

using System.Runtime.CompilerServices;
using Quarry.Core.Abstractions;
using Quarry.Core.Documents;

/// <summary>
/// Декоратор стратегии чанкинга, реализующий Contextual Retrieval (обогащение каждого чанка контекстом документа/метаданных для повышения точности поиска).
/// </summary>
public sealed class ContextualChunker : IChunker
{
    private readonly IChunker _innerChunker;
    private readonly Func<Document, DocumentChunk, CancellationToken, Task<string>> _contextProvider;

    public string Strategy => $"contextual+{_innerChunker.Strategy}";
    public IChunker InnerChunker => _innerChunker;

    public ContextualChunker(
        IChunker innerChunker,
        Func<Document, DocumentChunk, CancellationToken, Task<string>> contextProvider)
    {
        _innerChunker = innerChunker ?? throw new ArgumentNullException(nameof(innerChunker));
        _contextProvider = contextProvider ?? throw new ArgumentNullException(nameof(contextProvider));
    }

    public ContextualChunker(
        IChunker innerChunker,
        Func<Document, DocumentChunk, string> synchronousContextProvider)
        : this(
            innerChunker,
            (doc, chunk, _) => Task.FromResult(synchronousContextProvider(doc, chunk)))
    {
    }

    public ContextualChunker(
        IChunker innerChunker,
        string staticContextPrefix)
        : this(
            innerChunker,
            (_, _, _) => Task.FromResult(staticContextPrefix))
    {
    }

    public async IAsyncEnumerable<DocumentChunk> ChunkAsync(
        Document document,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        await foreach (var chunk in _innerChunker.ChunkAsync(document, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var context = await _contextProvider(document, chunk, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(context))
            {
                yield return chunk;
                continue;
            }

            var enrichedText = $"{context.Trim()}\n\n{chunk.Text}";

            var enrichedMetadata = new Dictionary<string, string>();
            if (chunk.Metadata is not null)
            {
                foreach (var (k, v) in chunk.Metadata)
                {
                    enrichedMetadata[k] = v;
                }
            }

            enrichedMetadata["is_contextual"] = "true";
            enrichedMetadata["context_prefix"] = context.Trim();

            yield return new DocumentChunk(
                chunk.ChunkId,
                chunk.DocumentId,
                chunk.Index,
                enrichedText,
                enrichedMetadata,
                chunk.StartCharOffset,
                chunk.EndCharOffset);
        }
    }
}
