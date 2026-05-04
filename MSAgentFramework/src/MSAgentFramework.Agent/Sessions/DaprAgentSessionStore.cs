using System.Text.Json;
using Dapr.Client;
using Microsoft.Agents.AI;

namespace MSAgentFramework.Agent.Sessions;

public sealed class DaprAgentSessionStore(DaprClient dapr) : IAgentSessionStore
{
    private const string StoreName = "agent-memory";
    private const string KeyPrefix = "agent-thread:";
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(7);

    private static readonly Dictionary<string, string> StateMetadata = new()
    {
        ["ttlInSeconds"] = ((int)Ttl.TotalSeconds).ToString()
    };

    public async Task<(string SessionId, AgentSession Session)> LoadOrCreateAsync(
        AIAgent agent, string? sessionId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            var newId = Guid.NewGuid().ToString("N");
            var fresh = await agent.CreateSessionAsync(ct).ConfigureAwait(false);
            return (newId, fresh);
        }

        var key = KeyPrefix + sessionId;
        var serialized = await dapr.GetStateAsync<string>(StoreName, key, cancellationToken: ct).ConfigureAwait(false);

        if (string.IsNullOrEmpty(serialized))
        {
            var fresh = await agent.CreateSessionAsync(ct).ConfigureAwait(false);
            return (sessionId, fresh);
        }

        var element = JsonSerializer.Deserialize<JsonElement>(serialized);
        var session = await agent.DeserializeSessionAsync(element, jsonSerializerOptions: null, ct).ConfigureAwait(false);
        return (sessionId, session);
    }

    public async Task SaveAsync(AIAgent agent, string sessionId, AgentSession session, CancellationToken ct)
    {
        var element = await agent.SerializeSessionAsync(session, jsonSerializerOptions: null, ct).ConfigureAwait(false);
        var serialized = JsonSerializer.Serialize(element);
        await dapr.SaveStateAsync(StoreName, KeyPrefix + sessionId, serialized,
            metadata: StateMetadata, cancellationToken: ct).ConfigureAwait(false);
    }
}
