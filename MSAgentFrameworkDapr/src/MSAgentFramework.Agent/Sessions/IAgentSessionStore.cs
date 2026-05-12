using Microsoft.Agents.AI;

namespace MSAgentFramework.Agent.Sessions;

public interface IAgentSessionStore
{
    Task<(string SessionId, AgentSession Session)> LoadOrCreateAsync(
        AIAgent agent, string? sessionId, CancellationToken ct);

    Task SaveAsync(AIAgent agent, string sessionId, AgentSession session, CancellationToken ct);
}
