using System.Text.Json;
using Dapr.Client;
using Microsoft.Agents.AI;
using MSAgentFramework.Agent.Sessions;
using NSubstitute;

namespace MSAgentFramework.Agent.Tests.Sessions;

public sealed class DaprAgentSessionStoreTests
{
    private const string StoreName = "agent-memory";
    private const string KeyPrefix = "agent-thread:";

    [Fact]
    public async Task LoadOrCreate_NoSessionId_GeneratesIdAndCreatesSession()
    {
        var dapr = Substitute.For<DaprClient>();
        var agent = Substitute.For<AIAgent>();
        var freshSession = Substitute.For<AgentSession>();
        agent.CreateSessionAsync(Arg.Any<CancellationToken>()).Returns(freshSession);

        var store = new DaprAgentSessionStore(dapr);

        var (id, session) = await store.LoadOrCreateAsync(agent, sessionId: null, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.Same(freshSession, session);
        await dapr.DidNotReceive().GetStateAsync<string>(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ConsistencyMode?>(),
            Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadOrCreate_KnownIdMissingState_CreatesFreshSessionWithSameId()
    {
        var dapr = Substitute.For<DaprClient>();
        var agent = Substitute.For<AIAgent>();
        var freshSession = Substitute.For<AgentSession>();

        dapr.GetStateAsync<string>(StoreName, KeyPrefix + "abc", Arg.Any<ConsistencyMode?>(),
            Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(string.Empty);
        agent.CreateSessionAsync(Arg.Any<CancellationToken>()).Returns(freshSession);

        var store = new DaprAgentSessionStore(dapr);

        var (id, session) = await store.LoadOrCreateAsync(agent, "abc", CancellationToken.None);

        Assert.Equal("abc", id);
        Assert.Same(freshSession, session);
    }

    [Fact]
    public async Task Save_WritesToStateStoreWithTtlMetadata()
    {
        var dapr = Substitute.For<DaprClient>();
        var agent = Substitute.For<AIAgent>();
        var session = Substitute.For<AgentSession>();
        var serialized = JsonDocument.Parse("""{"messages":[]}""").RootElement;
        agent.SerializeSessionAsync(session, Arg.Any<JsonSerializerOptions>(), Arg.Any<CancellationToken>())
            .Returns(serialized);

        var store = new DaprAgentSessionStore(dapr);
        await store.SaveAsync(agent, "abc", session, CancellationToken.None);

        await dapr.Received(1).SaveStateAsync(
            StoreName,
            KeyPrefix + "abc",
            Arg.Any<string>(),
            Arg.Any<StateOptions>(),
            Arg.Is<IReadOnlyDictionary<string, string>>(m =>
                m.ContainsKey("ttlInSeconds") && m["ttlInSeconds"] == "604800"),
            Arg.Any<CancellationToken>());
    }
}
