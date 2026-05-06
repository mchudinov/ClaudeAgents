using Dapr.AI.Microsoft.Extensions;
using Dapr.Client;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using MSAgentFramework.Agent.Contracts;
using MSAgentFramework.Agent.Sessions;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSingleton(_ => new DaprClientBuilder().Build());
builder.Services.AddDaprChatClient("anthropic-llm", configure: _ => { });
builder.Services.AddSingleton<IAgentSessionStore, DaprAgentSessionStore>();

var persona = Persona.Load("dotnet-csharp-expert");

builder.Services.AddSingleton<AIAgent>(sp => new ChatClientAgent(
    chatClient: sp.GetRequiredService<IChatClient>(),
    instructions: persona.Instructions,
    name: persona.Name,
    description: "Idiomatic modern C# / .NET advisory agent. No filesystem or shell access."));

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapMcp("/mcp");

app.Run();

public partial class Program;
