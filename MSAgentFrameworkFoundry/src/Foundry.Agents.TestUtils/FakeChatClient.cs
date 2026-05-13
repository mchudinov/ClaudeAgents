using Microsoft.Extensions.AI;

namespace Foundry.Agents.TestUtils;

/// <summary>Records all GetResponseAsync calls and replays a queued response.</summary>
public sealed class FakeChatClient : IChatClient
{
    private readonly Queue<string> _responses = new();
    public List<(IList<ChatMessage> Messages, ChatOptions? Options)> Calls { get; } = new();
    public void QueueResponse(string text) => _responses.Enqueue(text);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        Calls.Add((list, options));
        var text = _responses.Count > 0
            ? _responses.Dequeue()
            : throw new InvalidOperationException("FakeChatClient.QueueResponse was not called before GetResponseAsync");
        var msg = new ChatMessage(ChatRole.Assistant, text);
        return Task.FromResult(new ChatResponse(msg));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Streaming not exercised in tests");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
