namespace Quarry.Retrieval;

using Quarry.Core.Abstractions;
using Quarry.Core.Documents;

/// <summary>
/// Гибридный ретривер: параллельно запрашивает плотный (dense) и разреженный (sparse) ретриверы,
/// после чего объединяет ранжированные списки результатов по алгоритму RRF.
/// </summary>
public sealed class HybridRetriever : IRetriever
{
    private readonly IRetriever _dense;
    private readonly IRetriever _sparse;
    private readonly RrfMerger _merger;

    public HybridRetriever(IRetriever dense, IRetriever sparse, RrfMerger? merger = null)
    {
        _dense = dense ?? throw new ArgumentNullException(nameof(dense));
        _sparse = sparse ?? throw new ArgumentNullException(nameof(sparse));
        _merger = merger ?? new RrfMerger();
    }

    public string Strategy => $"hybrid({_dense.Strategy}+{_sparse.Strategy})";

    public async Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        string query,
        int topK,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);

        var candidateK = Math.Max(topK * 2, topK + 5);
        var denseTask = _dense.RetrieveAsync(query, candidateK, cancellationToken);
        var sparseTask = _sparse.RetrieveAsync(query, candidateK, cancellationToken);

        await Task.WhenAll(denseTask, sparseTask).WaitAsync(cancellationToken).ConfigureAwait(false);

        return _merger.Merge(denseTask.Result, sparseTask.Result, topK);
    }
}
