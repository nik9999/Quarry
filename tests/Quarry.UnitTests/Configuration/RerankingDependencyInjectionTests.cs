using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Quarry.Core.Abstractions;
using Quarry.Core.Configuration;
using Quarry.Reranking.DependencyInjection;
using Xunit;

namespace Quarry.Reranking.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddQuarryReranking_registers_noop_by_default()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRetriever>(new StubRetriever([]));
        services.AddQuarryReranking();

        using var sp = services.BuildServiceProvider();
        var retriever = sp.GetRequiredService<IRetriever>();
        var middleware = Assert.IsType<RerankingMiddleware>(retriever);

        Assert.Equal("stub-retriever+rerank(no-op)", retriever.Strategy);
        Assert.Equal(20, middleware.CandidateCount);
        Assert.Equal(0.0, middleware.MinScore);
    }

    [Fact]
    public void AddQuarryReranking_with_configure_action()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRetriever>(new StubRetriever([]));
        services.AddQuarryReranking(options =>
        {
            options.CandidateCount = 42;
            options.MinScore = 0.5;
        });

        using var sp = services.BuildServiceProvider();
        var middleware = Assert.IsType<RerankingMiddleware>(sp.GetRequiredService<IRetriever>());

        Assert.Equal(42, middleware.CandidateCount);
        Assert.Equal(0.5, middleware.MinScore);
    }

    [Fact]
    public void AddQuarryReranking_with_QuarryOptions_in_container()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRetriever>(new StubRetriever([]));
        services.Configure<QuarryOptions>(options =>
        {
            options.Reranking.CandidateCount = 50;
            options.Reranking.MinScore = 0.75;
        });
        services.AddQuarryReranking();

        using var sp = services.BuildServiceProvider();
        var middleware = Assert.IsType<RerankingMiddleware>(sp.GetRequiredService<IRetriever>());

        Assert.Equal(50, middleware.CandidateCount);
        Assert.Equal(0.75, middleware.MinScore);
    }

    [Fact]
    public void AddQuarryReranking_with_llm_mode()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new StubChatClient(_ => "0.8"));
        services.AddSingleton<IRetriever>(new StubRetriever([]));
        services.AddQuarryReranking(options => options.Mode = "llm");

        using var sp = services.BuildServiceProvider();
        var reranker = sp.GetRequiredService<IReranker>();

        Assert.IsType<LlmRerank>(reranker);
    }

    [Fact]
    public void AddQuarryReranking_with_onnx_mode_without_model_throws()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRetriever>(new StubRetriever([]));
        services.AddQuarryReranking(options => options.Mode = "onnx");

        using var sp = services.BuildServiceProvider();
        var ex = Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<IReranker>());
        Assert.Contains("requires an ICrossEncoderModel registration", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddQuarryReranking_with_onnx_mode_with_model_succeeds()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICrossEncoderModel>(new StubCrossEncoderModel(_ => 0.5f, "test-model"));
        services.AddSingleton<IRetriever>(new StubRetriever([]));
        services.AddQuarryReranking(options => options.Mode = "onnx");

        using var sp = services.BuildServiceProvider();
        var reranker = sp.GetRequiredService<IReranker>();

        var onnxReranker = Assert.IsType<OnnxCrossEncoderReranker>(reranker);
        Assert.Equal("onnx-cross-encoder(test-model)", onnxReranker.Implementation);
    }

    [Fact]
    public void AddQuarryReranking_unknown_mode_throws()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRetriever>(new StubRetriever([]));
        services.AddQuarryReranking(options => options.Mode = "unknown-xyz");

        using var sp = services.BuildServiceProvider();
        var ex = Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<IReranker>());
        Assert.Contains("Unknown reranking mode", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddQuarryReranking_throws_when_no_retriever_registered()
    {
        var services = new ServiceCollection();

        // Scrutor throws DecorationException immediately when Decorate cannot find a registered service of type IRetriever
        Assert.Throws<Scrutor.DecorationException>(() => services.AddQuarryReranking());
    }
}
