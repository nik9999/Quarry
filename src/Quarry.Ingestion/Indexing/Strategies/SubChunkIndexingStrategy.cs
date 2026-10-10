namespace Quarry.Ingestion.Indexing.Strategies;

using System.Globalization;
using System.Runtime.CompilerServices;
using Quarry.Core.Abstractions;
using Quarry.Core.Documents;

/// <summary>
/// Стратегия двухуровневой индексации (Parent-Child): документ разбивается на крупные родительские чанки,
/// а затем каждый родитель разбивается на компактные дочерние суб-чанки для максимально точного векторного поиска.
/// </summary>
public sealed class SubChunkIndexingStrategy : IIndexingStrategy
{
    private readonly IChunker _parentChunker;
    private readonly IChunker _childChunker;
    private readonly bool _indexParentAsWell;
    private readonly EmbeddingPrompt? _prompt;

    public string StrategyName => $"subchunk(parent:{_parentChunker.Strategy},child:{_childChunker.Strategy})";

    public SubChunkIndexingStrategy(
        IChunker parentChunker,
        IChunker childChunker,
        bool indexParentAsWell = false,
        EmbeddingPrompt? prompt = null)
    {
        _parentChunker = parentChunker ?? throw new ArgumentNullException(nameof(parentChunker));
        _childChunker = childChunker ?? throw new ArgumentNullException(nameof(childChunker));
        _indexParentAsWell = indexParentAsWell;
        _prompt = prompt;
    }

    public async IAsyncEnumerable<IndexedItem> ProcessAsync(
        Document document,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var childGlobalIndex = 0;

        await foreach (var parent in _parentChunker.ChunkAsync(document, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_indexParentAsWell)
            {
                var parentMeta = new Dictionary<string, string>(parent.Metadata ?? new Dictionary<string, string>())
                {
                    ["is_parent"] = "true"
                };

                var parentChunk = new DocumentChunk(
                    parent.ChunkId,
                    parent.DocumentId,
                    parent.Index,
                    parent.Text,
                    parentMeta,
                    parent.StartCharOffset,
                    parent.EndCharOffset);

                yield return new IndexedItem(parentChunk, _prompt, parentChunk.ChunkId, parentMeta);
            }

            // Create temporary document representing parent
            var parentDoc = new Document(parent.ChunkId, parent.Text, parent.Metadata);

            await foreach (var child in _childChunker.ChunkAsync(parentDoc, cancellationToken).ConfigureAwait(false))
            {
                var childMeta = new Dictionary<string, string>();
                if (child.Metadata is not null)
                {
                    foreach (var (k, v) in child.Metadata)
                    {
                        childMeta[k] = v;
                    }
                }

                childMeta["parent_id"] = parent.ChunkId;
                childMeta["parent_text"] = parent.Text;
                childMeta["child_subindex"] = child.Index.ToString(CultureInfo.InvariantCulture);

                var absoluteStart = parent.StartCharOffset + child.StartCharOffset;
                var absoluteEnd = parent.StartCharOffset + child.EndCharOffset;

                var finalChild = new DocumentChunk(
                    $"{document.Id}#c_{childGlobalIndex++}",
                    document.Id,
                    childGlobalIndex - 1,
                    child.Text,
                    childMeta,
                    absoluteStart,
                    absoluteEnd);

                yield return new IndexedItem(finalChild, _prompt, finalChild.ChunkId, childMeta);
            }
        }
    }
}
