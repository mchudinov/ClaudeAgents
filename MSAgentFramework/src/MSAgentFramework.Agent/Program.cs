using Anthropic.SDK;
using Dapr.Client;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using MSAgentFramework.Agent.Contracts;
using MSAgentFramework.Agent.Mcp;
using MSAgentFramework.Agent.Sessions;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSingleton(_ => new DaprClientBuilder().Build());

// Direct Anthropic IChatClient. Bypasses Dapr's conversation API so we
// control tool_choice / parallel-tool-use and can target claude-opus-4-7,
// which rejects the `temperature` Dapr always sends. Dapr is still used
// for state (agent-memory) and secrets. AnthropicClient reads the API key
// from ANTHROPIC_API_KEY (set by the AppHost).
builder.Services.AddHttpClient<AnthropicClient>();
var modelId = builder.Configuration["ANTHROPIC_MODEL"] ?? "claude-opus-4-7";
builder.Services.AddSingleton<IChatClient>(sp =>
    sp.GetRequiredService<AnthropicClient>().Messages
        .AsBuilder()
        .ConfigureOptions(o => o.ModelId ??= modelId)
        .Build());

builder.Services.AddSingleton<IAgentSessionStore, DaprAgentSessionStore>();

var persona = Persona.Load("dotnet-csharp-expert");

using var startupLoggerFactory = LoggerFactory.Create(lb => lb.AddConsole());
var (context7Client, context7Tools) = await Context7Tools.ConnectAsync(
    builder.Configuration["CONTEXT7_API_KEY"],
    startupLoggerFactory.CreateLogger("Context7"));

if (context7Client is not null)
{
    builder.Services.AddSingleton(context7Client);
}
builder.Services.AddSingleton(context7Tools);

builder.Services.AddScoped<AIAgent>(sp =>
{
    var innerChat = sp.GetRequiredService<IChatClient>();
    var chatClient = innerChat.AsBuilder().UseFunctionInvocation().Build();
    var tools = sp.GetRequiredService<IReadOnlyList<AITool>>();
    var instructions = tools.Count == 0
        ? persona.Instructions
        : persona.Instructions + Context7Tools.InstructionAddendum;
    return new ChatClientAgent(
        chatClient: chatClient,
        instructions: instructions,
        name: persona.Name,
        description: "Idiomatic modern C# / .NET advisory agent. May consult Context7 for current library/framework docs. No filesystem or shell access.",
        tools: tools.Count == 0 ? null : [.. tools]);
});

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapMcp("/mcp");

app.Run();

public partial class Program;
