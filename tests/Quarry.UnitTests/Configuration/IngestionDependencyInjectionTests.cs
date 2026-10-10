namespace Quarry.Ingestion.Tests.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;
using Quarry.Core.Abstractions;
using Quarry.Ingestion.Chunking;
using Quarry.Ingestion.DependencyInjection;
using Quarry.Ingestion.Embeddings;
using Quarry.Ingestion.Events;
using Quarry.Ingestion.Indexing;
using Quarry.Ingestion.Multimodal;
using Quarry.Ingestion.Tests.Fakes;
using Xunit;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddQuarryIngestion_RegistersAllExpectedServices()
    {
        var services = new ServiceCollection();

        // Register core dependencies needed by pipeline
        services.AddSingleton<IEmbeddingService>(sp =>
        {
            var gen = new FakeEmbeddingGenerator(384);
            return new EmbeddingService(gen, "model-test", 384);
        });
        services.AddSingleton<IVectorStore, FakeVectorStore>();

        services.AddQuarryIngestion(options =>
        {
            options.BatchSize = 16;
            options.DefaultChunker = new FixedSizeChunker(100, 20);
            options.EnableChangeFeedService = true;
        });

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IChunker>());
        Assert.NotNull(provider.GetService<IIndexingStrategy>());
        Assert.NotNull(provider.GetService<IIndexingPipeline>());
        Assert.NotNull(provider.GetService<IChangeFeed>());
        Assert.NotNull(provider.GetService<IPdfImageExtractor>());
        Assert.NotNull(provider.GetService<IImageCaptioner>());
        Assert.NotNull(provider.GetService<MultimodalChunker>());
    }
}
