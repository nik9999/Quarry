namespace Quarry.Retrieval;

using Quarry.Core.Abstractions;
using Quarry.Core.Documents;
using Quarry.Core.Filtering;

/// <summary>
/// Плотный (векторный) ретривер: преобразует поисковый запрос в вектор с использованием <see cref="IEmbeddingService.EmbedQueryAsync"/>
/// и запрашивает ближайших соседей из хранилища <see cref="IVectorStore"/>.
/// </summary>
public sealed class DenseRetriever : IRetriever
{
    private readonly IEmbeddingService _embeddings;
    private readonly IVectorStore _store;
    private readonly MetadataFilter? _filter;

    public DenseRetriever(IEmbeddingService embeddings, IVectorStore store, MetadataFilter? filter = null)
    {
        _embeddings = embeddings ?? throw new ArgumentNullException(nameof(embeddings));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _filter = filter;
    }

    public string Strategy => "dense";

    public async Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        string query,
        int topK,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);

        var queryVec = await _embeddings.EmbedQueryAsync(query, cancellationToken).ConfigureAwait(false);
        return await _store.SearchAsync(queryVec, topK, _filter, cancellationToken).ConfigureAwait(false);
    }
}
