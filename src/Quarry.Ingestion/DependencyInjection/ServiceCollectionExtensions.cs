namespace Quarry.Ingestion.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quarry.Core.Abstractions;
using Quarry.Ingestion.Chunking;
using Quarry.Ingestion.Events;
using Quarry.Ingestion.Indexing;
using Quarry.Ingestion.Indexing.Strategies;
using Quarry.Ingestion.Multimodal;

/// <summary>
/// Параметры конфигурации слоя инжекции и предобработки документов Quarry.
/// </summary>
public sealed class IngestionOptions
{
    /// <summary>
    /// Стратегия чанкинга по умолчанию (по умолчанию: RecursiveCharacterChunker 500/50).
    /// </summary>
    public IChunker? DefaultChunker { get; set; }

    /// <summary>
    /// Размер пакета при векторизации и сохранении в хранилище (по умолчанию: 32).
    /// </summary>
    public int BatchSize { get; set; } = 32;

    /// <summary>
    /// Включать ли фоновую службу Change Feed для автоматической переиндексации (по умолчанию: false).
    /// </summary>
    public bool EnableChangeFeedService { get; set; }
}

/// <summary>
/// Методы расширения IServiceCollection для регистрации компонентов Ingestion.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует компоненты подсистемы Ingestion (чанкинг, индексация, мультимодальная обработка, change feed).
    /// </summary>
    public static IServiceCollection AddQuarryIngestion(
        this IServiceCollection services,
        Action<IngestionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new IngestionOptions();
        configure?.Invoke(options);

        // Chunker
        var defaultChunker = options.DefaultChunker ?? new RecursiveCharacterChunker();
        services.TryAddSingleton<IChunker>(defaultChunker);

        // Indexing Strategy
        services.TryAddSingleton<IIndexingStrategy>(sp =>
        {
            var chunker = sp.GetRequiredService<IChunker>();
            return new ChunkIndexingStrategy(chunker);
        });

        // Indexing Pipeline
        services.TryAddSingleton<IIndexingPipeline>(sp =>
        {
            var strategy = sp.GetRequiredService<IIndexingStrategy>();
            var embeddingService = sp.GetRequiredService<IEmbeddingService>();
            var vectorStore = sp.GetRequiredService<IVectorStore>();
            return new IndexingPipeline(strategy, embeddingService, vectorStore, options.BatchSize);
        });

        // Multimodal
        services.TryAddSingleton<IPdfImageExtractor, PdfPigImageExtractor>();
        services.TryAddSingleton<IImageCaptioner, DefaultImageCaptioner>();
        services.TryAddSingleton<MultimodalChunker>();

        // Change Feed
        services.TryAddSingleton<IChangeFeed, ChannelChangeFeed>();

        if (options.EnableChangeFeedService)
        {
            services.AddHostedService<DocumentReingestService>();
        }

        return services;
    }
}
