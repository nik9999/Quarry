namespace Quarry.Retrieval.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quarry.Core.Abstractions;
using Quarry.Retrieval.VectorStores;

/// <summary>
/// Параметры настройки подсистемы поиска (Retrieval).
/// </summary>
public sealed class RetrievalOptions
{
    /// <summary>
    /// Режим поиска по умолчанию: "dense", "sparse", "hybrid" (по умолчанию: "dense").
    /// </summary>
    public string DefaultMode { get; set; } = "dense";

    /// <summary>
    /// Минимальный порог релевантности для отсечения шума (0.0 — фильтр выключен).
    /// </summary>
    public double MinScore { get; set; }

    /// <summary>
    /// Включать ли переупорядочивание контекста Lost-in-the-middle (по умолчанию: false).
    /// </summary>
    public bool EnableContextOrdering { get; set; }
}

/// <summary>
/// Методы расширения для регистрации сервисов поиска и хранилищ в контейнере зависимостей.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует <see cref="SqliteVectorStore"/> как реализацию <see cref="IVectorStore"/>.
    /// </summary>
    public static IServiceCollection AddSqliteVectorStore(
        this IServiceCollection services,
        Action<SqliteVectorStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new SqliteVectorStoreOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<IVectorStore, SqliteVectorStore>();

        return services;
    }

    /// <summary>
    /// Регистрирует легковесное векторное хранилище <see cref="InMemoryVectorStore"/> в оперативной памяти.
    /// </summary>
    public static IServiceCollection AddInMemoryVectorStore(
        this IServiceCollection services,
        string collectionName = "in-memory")
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IVectorStore>(_ => new InMemoryVectorStore(collectionName));
        return services;
    }

    /// <summary>
    /// Регистрирует плотный векторный ретривер <see cref="DenseRetriever"/>.
    /// </summary>
    public static IServiceCollection AddDenseRetriever(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<DenseRetriever>();
        return services;
    }

    /// <summary>
    /// Регистрирует разреженный лексический ретривер <see cref="SparseRetriever"/>.
    /// </summary>
    public static IServiceCollection AddSparseRetriever(
        this IServiceCollection services,
        double k1 = 1.5,
        double b = 0.75)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(new SparseRetriever(k1, b));
        return services;
    }

    /// <summary>
    /// Регистрирует гибридный ретривер <see cref="HybridRetriever"/> и слияние RRF.
    /// </summary>
    public static IServiceCollection AddHybridRetriever(
        this IServiceCollection services,
        int rrfK = 60)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDenseRetriever();
        services.AddSparseRetriever();
        services.TryAddSingleton(new RrfMerger(rrfK));

        services.TryAddSingleton<HybridRetriever>(sp =>
        {
            var dense = sp.GetRequiredService<DenseRetriever>();
            var sparse = sp.GetRequiredService<SparseRetriever>();
            var merger = sp.GetRequiredService<RrfMerger>();
            return new HybridRetriever(dense, sparse, merger);
        });

        return services;
    }

    /// <summary>
    /// Комплексная регистрация подсистемы поиска Quarry.
    /// </summary>
    public static IServiceCollection AddQuarryRetrieval(
        this IServiceCollection services,
        Action<RetrievalOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new RetrievalOptions();
        configure?.Invoke(options);

        services.AddDenseRetriever();
        services.AddSparseRetriever();
        services.AddHybridRetriever();

        services.TryAddSingleton<IRetriever>(sp =>
        {
            var dense = sp.GetRequiredService<DenseRetriever>();
            var sparse = sp.GetRequiredService<SparseRetriever>();
            var merger = sp.GetRequiredService<RrfMerger>();

            IRetriever baseRetriever = RetrieverModeFactory.Create(options.DefaultMode, dense, sparse, merger);

            if (options.MinScore > 0.0)
            {
                baseRetriever = new MinScoreFilter(baseRetriever, options.MinScore);
            }

            return baseRetriever;
        });

        return services;
    }
}
