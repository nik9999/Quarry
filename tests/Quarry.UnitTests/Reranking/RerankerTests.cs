using System.Net;
using System.Text.Json;
using Quarry.Core.Abstractions;
using Quarry.Core.Documents;
using Xunit;

namespace Quarry.Reranking.Tests;

public sealed class RerankerTests
{
    [Fact]
    public async Task NoOpReranker_preserves_order_and_truncates_to_topK()
    {
        IReadOnlyList<RetrievalResult> input =
        [
            new(TestData.CreateChunk("a", "x"), 0.9),
            new(TestData.CreateChunk("b", "x"), 0.8),
            new(TestData.CreateChunk("c", "x"), 0.7),
        ];
        var reranker = new NoOpReranker();
        var output = await reranker.RerankAsync("query", input, topK: 2);

        Assert.Equal(2, output.Count);
        Assert.Equal("a", output[0].Chunk.DocumentId);
        Assert.Equal("b", output[1].Chunk.DocumentId);
        Assert.Equal("no-op", reranker.Implementation);
    }

    [Fact]
    public async Task NoOpReranker_returns_empty_for_empty_candidates()
    {
        var reranker = new NoOpReranker();
        var output = await reranker.RerankAsync("query", [], topK: 5);
        Assert.Empty(output);
    }

    [Fact]
    public async Task NoOpReranker_throws_on_invalid_arguments()
    {
        var reranker = new NoOpReranker();
        IReadOnlyList<RetrievalResult> valid = [new(TestData.CreateChunk("a", "x"), 0.9)];

        await Assert.ThrowsAsync<ArgumentException>(() => reranker.RerankAsync("", valid, 5));
        await Assert.ThrowsAsync<ArgumentNullException>(() => reranker.RerankAsync("q", null!, 5));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => reranker.RerankAsync("q", valid, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => reranker.RerankAsync("q", valid, -1));
    }

    [Fact]
    public async Task LlmRerank_resorts_by_LLM_relevance_score()
    {
        var chat = new StubChatClient(prompt =>
        {
            if (prompt.Contains("Passage: about vacation policy", StringComparison.Ordinal))
            {
                return "0.95";
            }
            if (prompt.Contains("Passage: about remote work", StringComparison.Ordinal))
            {
                return "0.30";
            }
            return "0.10";
        });
        var reranker = new LlmRerank(chat);

        IReadOnlyList<RetrievalResult> input =
        [
            new(TestData.CreateChunk("remote", "about remote work"), 0.99),
            new(TestData.CreateChunk("vac", "about vacation policy"), 0.10),
            new(TestData.CreateChunk("none", "about something else"), 0.50),
        ];

        var output = await reranker.RerankAsync("vacation days", input, topK: 3);

        Assert.Equal(3, output.Count);
        Assert.Equal("vac", output[0].Chunk.DocumentId);
        Assert.Equal("remote", output[1].Chunk.DocumentId);
        Assert.Equal("none", output[2].Chunk.DocumentId);
        Assert.Equal(0.95, output[0].Score, precision: 2);
        Assert.Equal(0.30, output[1].Score, precision: 2);
        Assert.Equal("llm-as-judge", reranker.Implementation);
    }

    [Fact]
    public async Task LlmRerank_handles_unparseable_score_with_default()
    {
        var chat = new StubChatClient(_ => "I cannot provide a numeric score.");
        var reranker = new LlmRerank(chat);

        IReadOnlyList<RetrievalResult> input =
        [
            new(TestData.CreateChunk("a", "passage"), 0.1),
        ];

        var output = await reranker.RerankAsync("test", input, topK: 1);

        Assert.Single(output);
        Assert.Equal(0.5, output[0].Score, precision: 2);
    }

    [Fact]
    public async Task LlmRerank_throws_on_invalid_arguments()
    {
        var reranker = new LlmRerank(new StubChatClient(_ => "0.5"));
        IReadOnlyList<RetrievalResult> valid = [new(TestData.CreateChunk("a", "x"), 0.9)];

        await Assert.ThrowsAsync<ArgumentException>(() => reranker.RerankAsync("", valid, 5));
        await Assert.ThrowsAsync<ArgumentNullException>(() => reranker.RerankAsync("q", null!, 5));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => reranker.RerankAsync("q", valid, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => reranker.RerankAsync("q", valid, -1));
    }

    [Fact]
    public async Task OnnxCrossEncoderReranker_resorts_by_model_score()
    {
        var model = new StubCrossEncoderModel(text => text switch
        {
            "about vacation policy" => 0.95f,
            "about remote work" => 0.30f,
            _ => 0.10f,
        }, modelId: "bge-reranker-v2-m3");
        var reranker = new OnnxCrossEncoderReranker(model);

        IReadOnlyList<RetrievalResult> input =
        [
            new(TestData.CreateChunk("remote", "about remote work"), 0.99),
            new(TestData.CreateChunk("vac", "about vacation policy"), 0.10),
            new(TestData.CreateChunk("none", "about something else"), 0.50),
        ];

        var output = await reranker.RerankAsync("vacation days", input, topK: 2);

        Assert.Equal(2, output.Count);
        Assert.Equal("vac", output[0].Chunk.DocumentId);
        Assert.Equal("remote", output[1].Chunk.DocumentId);
        Assert.Equal(0.95f, (float)output[0].Score, precision: 5);
        Assert.Equal(0.30f, (float)output[1].Score, precision: 5);
        Assert.Equal("onnx-cross-encoder(bge-reranker-v2-m3)", reranker.Implementation);
    }

    [Fact]
    public async Task OnnxCrossEncoderReranker_returns_empty_for_empty_candidates()
    {
        var reranker = new OnnxCrossEncoderReranker(new StubCrossEncoderModel(_ => 0.5f));
        var output = await reranker.RerankAsync("query", [], topK: 5);
        Assert.Empty(output);
    }

    [Fact]
    public async Task OnnxCrossEncoderReranker_throws_on_invalid_arguments()
    {
        var reranker = new OnnxCrossEncoderReranker(new StubCrossEncoderModel(_ => 0.5f));
        IReadOnlyList<RetrievalResult> valid = [new(TestData.CreateChunk("a", "x"), 0.9)];

        await Assert.ThrowsAsync<ArgumentException>(() => reranker.RerankAsync("", valid, 5));
        await Assert.ThrowsAsync<ArgumentNullException>(() => reranker.RerankAsync("q", null!, 5));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => reranker.RerankAsync("q", valid, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => reranker.RerankAsync("q", valid, -1));
    }

    [Fact]
    public async Task CohereReranker_calls_api_and_resorts_by_relevance()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://api.cohere.com/v2/rerank", req.RequestUri?.ToString());
            Assert.Equal("Bearer test-api-key", req.Headers.Authorization?.ToString());

            var jsonResponse = """
            {
              "results": [
                { "index": 1, "relevance_score": 0.98 },
                { "index": 0, "relevance_score": 0.45 }
              ]
            }
            """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse, System.Text.Encoding.UTF8, "application/json"),
            };
        });

        using var client = new HttpClient(handler);
        var reranker = new CohereReranker(client, "test-api-key", "rerank-v3.5");

        IReadOnlyList<RetrievalResult> input =
        [
            new(TestData.CreateChunk("doc0", "passage 0"), 0.8),
            new(TestData.CreateChunk("doc1", "passage 1"), 0.6),
        ];

        var output = await reranker.RerankAsync("query", input, topK: 2);

        Assert.Equal(2, output.Count);
        Assert.Equal("doc1", output[0].Chunk.DocumentId);
        Assert.Equal(0.98, output[0].Score, precision: 2);
        Assert.Equal("doc0", output[1].Chunk.DocumentId);
        Assert.Equal(0.45, output[1].Score, precision: 2);
        Assert.Equal("cohere-rerank-v3.5", reranker.Implementation);
    }

    [Fact]
    public async Task CohereReranker_returns_empty_for_empty_candidates_without_calling_http()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called"));
        using var client = new HttpClient(handler);
        var reranker = new CohereReranker(client, "test-api-key");

        var output = await reranker.RerankAsync("query", [], topK: 5);
        Assert.Empty(output);
    }

    [Fact]
    public async Task CohereReranker_throws_on_invalid_arguments()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(handler);
        var reranker = new CohereReranker(client, "key");
        IReadOnlyList<RetrievalResult> valid = [new(TestData.CreateChunk("a", "x"), 0.9)];

        await Assert.ThrowsAsync<ArgumentException>(() => reranker.RerankAsync("", valid, 5));
        await Assert.ThrowsAsync<ArgumentNullException>(() => reranker.RerankAsync("q", null!, 5));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => reranker.RerankAsync("q", valid, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => reranker.RerankAsync("q", valid, -1));
    }

    [Fact]
    public async Task RerankingMiddleware_pulls_candidate_count_then_picks_topK()
    {
        var rawInput = Enumerable.Range(0, 20)
            .Select(i => new RetrievalResult(TestData.CreateChunk($"d{i}", $"text{i}"), 1.0 / (i + 1)))
            .ToList();
        var inner = new StubRetriever(rawInput);
        var reranker = new NoOpReranker();
        var middleware = new RerankingMiddleware(inner, reranker, candidateCount: 20);

        var output = await middleware.RetrieveAsync("query", topK: 5);

        Assert.Equal(5, output.Count);
        Assert.Equal(20, inner.LastTopK);
    }

    [Fact]
    public async Task RerankingMiddleware_handles_topK_larger_than_candidate_count()
    {
        var rawInput = Enumerable.Range(0, 30)
            .Select(i => new RetrievalResult(TestData.CreateChunk($"d{i}", $"text{i}"), 1.0 / (i + 1)))
            .ToList();
        var inner = new StubRetriever(rawInput);
        var reranker = new NoOpReranker();
        var middleware = new RerankingMiddleware(inner, reranker, candidateCount: 10);

        var output = await middleware.RetrieveAsync("query", topK: 25);

        Assert.Equal(25, output.Count);
        Assert.Equal(25, inner.LastTopK);
    }

    [Fact]
    public async Task RerankingMiddleware_strategy_composes_inner_and_reranker()
    {
        var inner = new StubRetriever([]);
        var reranker = new NoOpReranker();
        var middleware = new RerankingMiddleware(inner, reranker);

        Assert.Equal("stub-retriever+rerank(no-op)", middleware.Strategy);
        Assert.Equal(20, middleware.CandidateCount);
        Assert.Equal(0.0, middleware.MinScore);
    }

    [Fact]
    public async Task RerankingMiddleware_minScore_floor_drops_below_threshold()
    {
        IReadOnlyList<RetrievalResult> input =
        [
            new(TestData.CreateChunk("hi", "x"), 0.9),
            new(TestData.CreateChunk("mid", "x"), 0.2),
            new(TestData.CreateChunk("lo", "x"), 0.05),
        ];
        var inner = new StubRetriever(input);
        var reranker = new ScoreEchoReranker();
        var middleware = new RerankingMiddleware(inner, reranker, candidateCount: 20, minScore: 0.3);

        var output = await middleware.RetrieveAsync("query", topK: 5);

        Assert.Single(output);
        Assert.Equal("hi", output[0].Chunk.DocumentId);
    }

    [Fact]
    public async Task RerankingMiddleware_minScore_above_all_returns_empty()
    {
        IReadOnlyList<RetrievalResult> input =
        [
            new(TestData.CreateChunk("hi", "x"), 0.9),
            new(TestData.CreateChunk("mid", "x"), 0.2),
            new(TestData.CreateChunk("lo", "x"), 0.05),
        ];
        var inner = new StubRetriever(input);
        var reranker = new ScoreEchoReranker();
        var middleware = new RerankingMiddleware(inner, reranker, candidateCount: 20, minScore: 0.95);

        var output = await middleware.RetrieveAsync("query", topK: 5);

        Assert.Empty(output);
    }

    [Fact]
    public async Task RerankingMiddleware_throws_on_invalid_arguments()
    {
        var inner = new StubRetriever([]);
        var reranker = new NoOpReranker();

        Assert.Throws<ArgumentNullException>(() => new RerankingMiddleware(null!, reranker));
        Assert.Throws<ArgumentNullException>(() => new RerankingMiddleware(inner, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RerankingMiddleware(inner, reranker, candidateCount: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RerankingMiddleware(inner, reranker, candidateCount: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RerankingMiddleware(inner, reranker, minScore: -0.1));

        var middleware = new RerankingMiddleware(inner, reranker);
        await Assert.ThrowsAsync<ArgumentException>(() => middleware.RetrieveAsync("", 5));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => middleware.RetrieveAsync("q", 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => middleware.RetrieveAsync("q", -1));
    }
}
