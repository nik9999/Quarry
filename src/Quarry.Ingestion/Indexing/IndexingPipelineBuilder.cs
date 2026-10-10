namespace Quarry.Ingestion.Indexing;

using Quarry.Core.Abstractions;
using Quarry.Ingestion.Indexing.Strategies;

/// <summary>
/// Fluent Builder для конфигурирования и построения конвейера индексации <see cref="IIndexingPipeline"/>.
/// </summary>
public sealed class IndexingPipelineBuilder
{
    private IIndexingStrategy? _strategy;
    private IEmbeddingService? _embeddingService;
    private IVectorStore? _vectorStore;
    private int _batchSize = 32;

    public IndexingPipelineBuilder WithStrategy(IIndexingStrategy strategy)
    {
        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        return this;
    }

    public IndexingPipelineBuilder WithChunker(IChunker chunker, EmbeddingPrompt? prompt = null)
    {
        ArgumentNullException.ThrowIfNull(chunker);
        _strategy = new ChunkIndexingStrategy(chunker, prompt);
        return this;
    }

    public IndexingPipelineBuilder WithEmbeddingService(IEmbeddingService embeddingService)
    {
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        return this;
    }

    public IndexingPipelineBuilder WithVectorStore(IVectorStore vectorStore)
    {
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        return this;
    }

    public IndexingPipelineBuilder WithBatchSize(int batchSize)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be greater than zero.");
        }

        _batchSize = batchSize;
        return this;
    }

    public IIndexingPipeline Build()
    {
        if (_strategy is null)
        {
            throw new InvalidOperationException("Indexing strategy or chunker must be configured before building the pipeline.");
        }

        if (_embeddingService is null)
        {
            throw new InvalidOperationException("Embedding service must be configured before building the pipeline.");
        }

        if (_vectorStore is null)
        {
            throw new InvalidOperationException("Vector store must be configured before building the pipeline.");
        }

        return new IndexingPipeline(_strategy, _embeddingService, _vectorStore, _batchSize);
    }
}
