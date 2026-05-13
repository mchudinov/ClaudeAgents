namespace Foundry.Agents.Memory;

public interface ICosmosThreadStore
{
    Task<AgentThread> LoadOrCreateAsync(
        string threadId,
        AgentRole role,
        string personaMarkdown,
        CancellationToken ct);

    Task SaveAsync(AgentThread thread, CancellationToken ct);

    Task CompactAsync(string threadId, AgentRole role, string summary, CancellationToken ct);
}
