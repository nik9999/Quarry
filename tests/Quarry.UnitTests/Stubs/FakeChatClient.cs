namespace Quarry.Retrieval.Tests.Fakes;

using Microsoft.Extensions.AI;

public sealed class FakeChatClient : IChatClient
{
    private readonly Func<string, string> _responder;

    public ChatClientMetadata Metadata { get; } = new("fake-chat");
    public List<string> CapturedPrompts { get; } = new();

    public FakeChatClient(Func<string, string>? responder = null)
    {
        _responder = responder ?? (prompt => $"Response for: {prompt}");
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var lastMessage = messages.LastOrDefault()?.Text ?? string.Empty;
        CapturedPrompts.Add(lastMessage);

        var answer = _responder(lastMessage);
        var chatMessage = new ChatMessage(ChatRole.Assistant, answer);
        var response = new ChatResponse([chatMessage]);

        return Task.FromResult(response);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
