using Foundry.Agents.Contracts;
using Foundry.Agents.Memory;
using Microsoft.Extensions.AI;

namespace Foundry.Agents.Developer.Compactor;

public sealed class ThreadCompactor
{
    private const string SummaryPrompt =
        "Summarize this conversation in ≤500 tokens, preserving: the original task, the final PR URL, " +
        "key technical decisions, and any commitments to follow up. Output prose, not bullets.";

    private readonly ICosmosThreadStore _store;
    private readonly IChatClientFactory _chatFactory;

    public ThreadCompactor(ICosmosThreadStore store, IChatClientFactory chatFactory)
    {
        _store = store;
        _chatFactory = chatFactory;
    }

    public async Task<string> CompactAsync(AgentThread thread, EffortLevel effort, CancellationToken ct)
    {
        var client = _chatFactory.Create();
        var msgs = thread.Messages.Select(m => new ChatMessage(new ChatRole(m.Role), m.Content)).ToList();
        msgs.Add(new ChatMessage(ChatRole.User, SummaryPrompt));

        var response = await client.GetResponseAsync(msgs, ChatClientFactory.ChatOptionsFor(effort), ct);
        var summary = response.Text.Trim();

        await _store.CompactAsync(thread.Id, AgentRole.Developer, summary, ct);
        return summary;
    }
}
