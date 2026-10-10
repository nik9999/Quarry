namespace Quarry.Ingestion.Indexing;

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Quarry.Core.Abstractions;
using Quarry.Core.Documents;

/// <summary>
/// Реализация конвейера индексации документов.
/// </summary>
public sealed class IndexingPipeline : IIndexingPipeline
{
    private readonly IIndexingStrategy _strategy;
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly int _batchSize;

    public IndexingPipeline(
        IIndexingStrategy strategy,
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        int batchSize = 32)
    {
        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));

        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be greater than zero.");
        }

        _batchSize = batchSize;
    }

    public async Task<IndexingResult> IngestAsync(
        IEnumerable<Document> documents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        return await IngestAsync(ToAsyncEnumerable(documents, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public async Task<IndexingResult> IngestAsync(
        IAsyncEnumerable<Document> documents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var stopwatch = Stopwatch.StartNew();
        var totalDocs = 0;
        var totalChunks = 0;

        await _vectorStore.EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);

        var chunkBuffer = new List<DocumentChunk>(_batchSize);

        await foreach (var doc in documents.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            totalDocs++;

            await foreach (var item in _strategy.ProcessAsync(doc, cancellationToken).ConfigureAwait(false))
            {
                chunkBuffer.Add(item.Chunk);

                if (chunkBuffer.Count >= _batchSize)
                {
                    totalChunks += await FlushBatchAsync(chunkBuffer, cancellationToken).ConfigureAwait(false);
                    chunkBuffer.Clear();
                }
            }
        }

        if (chunkBuffer.Count > 0)
        {
            totalChunks += await FlushBatchAsync(chunkBuffer, cancellationToken).ConfigureAwait(false);
            chunkBuffer.Clear();
        }

        stopwatch.Stop();
        return new IndexingResult(totalDocs, totalChunks, stopwatch.Elapsed);
    }

    private async Task<int> FlushBatchAsync(
        List<DocumentChunk> batch,
        CancellationToken cancellationToken)
    {
        var count = batch.Count;
        var embeddedList = new List<EmbeddedChunk>(count);

        await foreach (var embedded in _embeddingService.EmbedAsync(ToAsyncEnumerable(batch, cancellationToken), cancellationToken).ConfigureAwait(false))
        {
            embeddedList.Add(embedded);
        }

        if (embeddedList.Count > 0)
        {
            await _vectorStore.UpsertAsync(embeddedList, cancellationToken).ConfigureAwait(false);
        }

        return count;
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        IEnumerable<T> source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var item in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }

        await Task.CompletedTask;
    }
}
