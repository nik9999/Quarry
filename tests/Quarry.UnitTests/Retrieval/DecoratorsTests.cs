namespace Quarry.Retrieval.Tests.Decorators;

using Quarry.Core.Abstractions;
using Quarry.Core.Documents;
using Quarry.Retrieval.Decorators;
using Quarry.Retrieval.Tests.Fakes;
using Xunit;

public class DecoratorsTests
{
    private sealed class EchoRetriever : IRetriever
    {
        public string Strategy => "echo";
        public string? LastQuery { get; private set; }

        public Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(string query, int topK, CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            var chunk = new DocumentChunk($"chunk_for_{query.GetHashCode():x}", "doc-1", 0, $"Content for: {query}");
            return Task.FromResult<IReadOnlyList<RetrievalResult>>([new RetrievalResult(chunk, 0.85)]);
        }
    }

    [Fact]
    public async Task HydeRetriever_GeneratesHypotheticalPassageAndRetrieves()
    {
        var inner = new EchoRetriever();
        var chat = new FakeChatClient(_ => "Гипотетический ответ на вопрос о базах данных.");
        var hyde = new HydeRetriever(inner, chat);

        var results = await hyde.RetrieveAsync("Что такое sqlite-vec?", 5);

        Assert.Single(results);
        Assert.Equal("Гипотетический ответ на вопрос о базах данных.", inner.LastQuery);
    }

    [Fact]
    public async Task StepBackRetriever_ExecutesBothQueriesAndFuses()
    {
        var inner = new EchoRetriever();
        var chat = new FakeChatClient(_ => "Каковы принципы векторных СУБД в целом?");
        var stepBack = new StepBackRetriever(inner, chat);

        var results = await stepBack.RetrieveAsync("Как работает sqlite-vec в C#?", 5);

        Assert.NotEmpty(results);
        Assert.StartsWith("step-back(", stepBack.Strategy);
    }

    [Fact]
    public async Task RagFusionRetriever_GeneratesQueryVariationsAndFuses()
    {
        var inner = new EchoRetriever();
        var chat = new FakeChatClient(_ => "вариант раз\nвариант два\nвариант три");
        var fusion = new RagFusionRetriever(inner, chat, queryCount: 3);

        var results = await fusion.RetrieveAsync("Запрос", 5);

        Assert.NotEmpty(results);
        Assert.StartsWith("rag-fusion(", fusion.Strategy);
    }

    [Fact]
    public async Task CragRetriever_ConfidenceRouting_CorrectAndIncorrect()
    {
        var highInner = new StaticRetriever([new(new DocumentChunk("c-high", "d-1", 0, "Top"), 0.90)]);
        var lowInner = new StaticRetriever([new(new DocumentChunk("c-low", "d-1", 0, "Low"), 0.20)]);
        var fallback = new StaticRetriever([new(new DocumentChunk("c-fallback", "d-1", 0, "Fallback"), 0.75)]);

        var cragHigh = new CragRetriever(highInner, fallback, upperThreshold: 0.7, lowerThreshold: 0.3);
        var highResults = await cragHigh.RetrieveAsync("test", 5);
        Assert.Single(highResults);
        Assert.Equal("c-high", highResults[0].Chunk.ChunkId);

        var cragLow = new CragRetriever(lowInner, fallback, upperThreshold: 0.7, lowerThreshold: 0.3);
        var lowResults = await cragLow.RetrieveAsync("test", 5);
        Assert.Single(lowResults);
        Assert.Equal("c-fallback", lowResults[0].Chunk.ChunkId);
    }

    private sealed class StaticRetriever : IRetriever
    {
        private readonly IReadOnlyList<RetrievalResult> _results;
        public string Strategy => "static";

        public StaticRetriever(IReadOnlyList<RetrievalResult> results) => _results = results;

        public Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(string query, int topK, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_results);
        }
    }
}
