using Microsoft.Extensions.DependencyInjection;
using Quarry.Core.Abstractions;
using Quarry.Retrieval.DependencyInjection;
using Quarry.Retrieval.VectorStores;
using Xunit;

namespace Quarry.Retrieval.Tests.DependencyInjection;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSqliteVectorStore_RegistersSingletonStoreAndOptions()
    {
        var services = new ServiceCollection();

        services.AddSqliteVectorStore(options =>
        {
            options.ConnectionString = "Data Source=:memory:";
            options.CollectionName = "di_chunks";
            options.Dimensions = 128;
        });

        using var provider = services.BuildServiceProvider();

        var options = provider.GetService<SqliteVectorStoreOptions>();
        Assert.NotNull(options);
        Assert.Equal("di_chunks", options.CollectionName);
        Assert.Equal(128, options.Dimensions);

        var store = provider.GetService<IVectorStore>();
        Assert.NotNull(store);
        Assert.IsType<SqliteVectorStore>(store);
        Assert.Equal("di_chunks", store.CollectionName);
    }

    [Fact]
    public void AddQuarryRetrieval_RegistersRetrieversAndConfiguredStrategy()
    {
        var services = new ServiceCollection();

        // Core dependency
        services.AddSingleton<IEmbeddingService>(new Fakes.FakeEmbeddingService());
        services.AddInMemoryVectorStore();

        services.AddQuarryRetrieval(options =>
        {
            options.DefaultMode = "hybrid";
            options.MinScore = 0.4;
        });

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<DenseRetriever>());
        Assert.NotNull(provider.GetService<SparseRetriever>());
        Assert.NotNull(provider.GetService<HybridRetriever>());

        var defaultRetriever = provider.GetService<IRetriever>();
        Assert.NotNull(defaultRetriever);
        Assert.StartsWith("min-score(hybrid(", defaultRetriever.Strategy);
    }
}
