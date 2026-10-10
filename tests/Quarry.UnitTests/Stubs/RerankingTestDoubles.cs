using System.Net;
using System.Text;
using Microsoft.Extensions.AI;
using Quarry.Core.Abstractions;
using Quarry.Core.Documents;

namespace Quarry.Reranking.Tests;

internal static class TestData
{
    public static DocumentChunk CreateChunk(string id, string text)
    {
        return new DocumentChunk($"{id}#0", id, 0, text, null, 0, text.Length);
    }
}

internal sealed class StubChatClient(Func<string, string> responder) : IChatClient
{
    public ChatClientMetadata Metadata { get; } = new("stub");

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var prompt = string.Join("\n", chatMessages.Select(m => m.Text));
        var text = responder(prompt);
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

internal sealed class StubCrossEncoderModel(Func<string, float> scorer, string modelId = "stub-cross-encoder") : ICrossEncoderModel
{
    public string ModelId => modelId;

    public IReadOnlyList<float> Score(string query, IReadOnlyList<string> documents)
    {
        return [.. documents.Select(scorer)];
    }
}

internal sealed class StubRetriever(IReadOnlyList<RetrievalResult> results) : IRetriever
{
    public int LastTopK { get; private set; }
    public string Strategy => "stub-retriever";

    public Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(string query, int topK, CancellationToken cancellationToken = default)
    {
        LastTopK = topK;
        IReadOnlyList<RetrievalResult> list = results.Take(topK).ToList();
        return Task.FromResult(list);
    }
}

internal sealed class ScoreEchoReranker : IReranker
{
    public string Implementation => "score-echo";

    public Task<IReadOnlyList<RetrievalResult>> RerankAsync(
        string query,
        IReadOnlyList<RetrievalResult> candidates,
        int topK,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RetrievalResult> r = [.. candidates.OrderByDescending(c => c.Score).Take(topK)];
        return Task.FromResult(r);
    }
}

internal sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(handler(request));
    }
}
