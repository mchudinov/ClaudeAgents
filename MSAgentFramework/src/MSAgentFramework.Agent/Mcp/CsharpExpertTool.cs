using System.ComponentModel;
using Microsoft.Agents.AI;
using ModelContextProtocol.Server;
using MSAgentFramework.Agent.Contracts;
using MSAgentFramework.Agent.Sessions;

namespace MSAgentFramework.Agent.Mcp;

[McpServerToolType]
public sealed class CsharpExpertTool(AIAgent agent, IAgentSessionStore sessions, ILogger<CsharpExpertTool> log)
{
    [McpServerTool(Name = "ask_dotnet_csharp_expert")]
    [Description(
        "Ask the .NET / C# expert for advice on idiomatic modern C#, BCL usage, " +
        "async patterns, LINQ, EF Core, ASP.NET Core, performance, or xUnit testing. " +
        "Advisory-only — the expert returns guidance and code snippets; the caller " +
        "is expected to apply any changes.")]
    public async Task<AskResult> AskAsync(
        [Description("The question, including code snippets if relevant.")]
        string question,
        [Description("Optional thread id returned by a previous call to continue the conversation.")]
        string? threadId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question))
            throw new ArgumentException("question must not be empty", nameof(question));

        var (sessionId, session) = await sessions.LoadOrCreateAsync(agent, threadId, ct);

        log.LogInformation("Running agent {Agent} on session {SessionId}", agent.GetType().Name, sessionId);

        var response = await agent.RunAsync(question, session, options: null, cancellationToken: ct);
        await sessions.SaveAsync(agent, sessionId, session, ct);

        return new AskResult(sessionId, response.Text ?? string.Empty);
    }
}
