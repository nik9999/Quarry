using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Quarry.Core.Abstractions;
using Quarry.Core.Configuration;

namespace Quarry.Reranking.DependencyInjection;

/// <summary>
/// DI registration for the Quarry reranking layer. Wires up the
/// configured <see cref="IReranker"/> and transparently decorates the already
/// registered <see cref="IRetriever"/> with <see cref="RerankingMiddleware"/>
/// so reranking is invisible to the rest of the pipeline.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the reranking stack and decorates the existing
    /// <see cref="IRetriever"/> with <see cref="RerankingMiddleware"/>.
    /// <para>
    /// Call this <strong>after</strong> an <see cref="IRetriever"/> has already
    /// been registered (e.g. via the retrieval DI extensions): it uses Scrutor's
    /// <c>Decorate</c> to wrap that existing registration, and the call throws if
    /// none is present.
    /// </para>
    /// <para>
    /// The active reranker is selected at resolve time from
    /// <see cref="QuarryOptions"/>.<see cref="QuarryOptions.Reranking"/> or
    /// direct <see cref="RerankingOptions"/>.<see cref="RerankingOptions.Mode"/>:
    /// <list type="bullet">
    ///   <item><c>none</c> → <see cref="NoOpReranker"/> (default, A/B control, fallback).</item>
    ///   <item><c>llm</c> → <see cref="LlmRerank"/> over the registered <see cref="IChatClient"/>.</item>
    ///   <item><c>cohere</c> → <see cref="CohereReranker"/> over the named <c>cohere</c> HttpClient + <c>COHERE_API_KEY</c>.</item>
    ///   <item><c>onnx</c> → <see cref="OnnxCrossEncoderReranker"/>, requires a registered <see cref="ICrossEncoderModel"/>
    ///         (add <c>Quarry.Reranking.Onnx</c> and call <c>AddQuarryOnnxCrossEncoder</c>).</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="services">The DI container being built.</param>
    /// <param name="configure">Optional configuration action for <see cref="RerankingOptions"/>.</param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    public static IServiceCollection AddQuarryReranking(
        this IServiceCollection services,
        Action<RerankingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddSingleton<NoOpReranker>();
        services.AddSingleton<LlmRerank>();

        services.AddSingleton<IReranker>(sp =>
        {
            var options = ResolveOptions(sp);
            var mode = options.Mode;
            return mode switch
            {
                "none" => sp.GetRequiredService<NoOpReranker>(),
                "llm" => sp.GetRequiredService<LlmRerank>(),
                "cohere" => BuildCohereReranker(sp),
                "onnx" => BuildOnnxReranker(sp),
                _ => throw new InvalidOperationException($"Unknown reranking mode: {mode}"),
            };
        });

        services.Decorate<IRetriever>((inner, sp) =>
        {
            var options = ResolveOptions(sp);
            return new RerankingMiddleware(
                inner,
                sp.GetRequiredService<IReranker>(),
                options.CandidateCount,
                options.MinScore);
        });

        return services;
    }

    private static RerankingOptions ResolveOptions(IServiceProvider sp)
    {
        var direct = sp.GetService<IOptions<RerankingOptions>>()?.Value;
        var umbrella = sp.GetService<IOptions<QuarryOptions>>()?.Value;

        if (direct is not null && (direct.Mode != "none" || direct.CandidateCount != 20 || direct.MinScore > 0.0))
        {
            return direct;
        }

        if (umbrella is not null && (umbrella.Reranking.Mode != "none" || umbrella.Reranking.CandidateCount != 20 || umbrella.Reranking.MinScore > 0.0))
        {
            return umbrella.Reranking;
        }

        return direct ?? umbrella?.Reranking ?? new RerankingOptions();
    }

    private static CohereReranker BuildCohereReranker(IServiceProvider sp)
    {
        var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("cohere");
        var apiKey = Environment.GetEnvironmentVariable("COHERE_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Reranking mode 'cohere' requires the COHERE_API_KEY environment variable to be set.");
        }
        return new CohereReranker(httpClient, apiKey);
    }

    private static OnnxCrossEncoderReranker BuildOnnxReranker(IServiceProvider sp)
    {
        var model = sp.GetService<ICrossEncoderModel>()
            ?? throw new InvalidOperationException(
                "Reranking mode 'onnx' requires an ICrossEncoderModel registration. " +
                "Reference the Quarry.Reranking.Onnx adapter and call " +
                "AddQuarryOnnxCrossEncoder(onnxModelPath, sentencePieceModelPath).");
        return new OnnxCrossEncoderReranker(model);
    }
}
