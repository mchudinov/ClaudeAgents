# MSAgentFramework — `dotnet-csharp-expert` remote agent

## Goal

Build a remote AI agent that exposes the **Microsoft Agent Framework** running on **.NET 10**, talks to **Claude Opus 4.7** through the **Dapr Conversation API** (`Dapr.AI`), persists thread memory in **Redis** through Dapr state, and is reachable from any Claude Code CLI session as a remote subagent over **MCP on port 8085**.

The agent's persona is a pure C# / .NET expert: idiomatic modern C# (records, pattern matching, primary constructors, collection expressions, required members), async/await, LINQ, BCL, generics, NRT, `Span<T>`/`Memory<T>`, EF Core, ASP.NET Core, xUnit testing. **No Docker / container / port-8081 content** — that lives in the existing `dotnet-docker-expert` mirror trio and is unrelated.

## Why this stack

- **Microsoft Agent Framework (MAF)** — the agent loop, threads, tool dispatch, MCP server hosting, multi-agent primitives. Talks to any model through `IChatClient`.
- **`Dapr.AI` (`DaprConversationClient`)** — provider-portable LLM gateway via the sidecar. Built-in prompt caching, PII obfuscation, retries, telemetry. Swap LLM providers by editing component YAML — no recompile.
- The two compose through a small `IChatClient` adapter (`DaprChatClient`, ~50 LOC) that bridges MAF's interface to Dapr's `ConverseAsync`. MAF on top → Dapr underneath.
- **.NET Aspire AppHost** orchestrates the local dev inner loop (agent + Dapr sidecar + Redis + components) with one `dotnet run`.
- **Redis** is the Dapr state store for agent thread memory.
- **MCP over Streamable HTTP on `:8085`** is the canonical "remote tool/agent for Claude Code" wire format.

## Solution layout

```
MSAgentFramework/
├─ MSAgentFramework.sln
├─ src/
│  ├─ MSAgentFramework.AppHost/          (.NET 10, Aspire AppHost)
│  ├─ MSAgentFramework.ServiceDefaults/  (shared OTel + health-check defaults)
│  ├─ MSAgentFramework.Agent/            (ASP.NET Core, MCP server on :8085)
│  └─ MSAgentFramework.Agent.Contracts/  (DTOs, persona constants)
├─ tests/
│  └─ MSAgentFramework.Agent.Tests/      (xUnit + WebApplicationFactory)
└─ deploy/
   ├─ Dockerfile                         (agent image only)
   ├─ docker-compose.yml                 (remote deploy: agent + daprd + redis + placement)
   └─ dapr/components/
      ├─ anthropic-llm.yaml              (conversation.anthropic, model=claude-opus-4-7)
      ├─ agent-memory.yaml               (state.redis)
      └─ envvar-secretstore.yaml         (env-var secret store)
```

## Runtime topology

Two runtime modes, **same agent image**:

1. **Local dev — `dotnet run --project src/MSAgentFramework.AppHost`**
   Aspire spins up Redis + the agent service + a Dapr sidecar via `Aspire.Hosting.Dapr` (`WithDaprSidecar`), wires components from `deploy/dapr/components/`, exposes the Aspire dashboard.
2. **Remote — `docker compose -f deploy/docker-compose.yml up -d`**
   Four containers: `agent` (publishes `8085:8085`), `daprd` sidecar (`network_mode: service:agent`, sharing localhost), `redis`, `placement`. Same component YAMLs mounted read-only.

**Port plan**

| Port | Service | Exposure |
|---|---|---|
| 8085 | agent MCP | published |
| 3500 | Dapr HTTP | internal |
| 50001 | Dapr gRPC | internal |
| 6379 | Redis | internal |
| 50005 | placement | internal |

The Dockerfile only packages the agent service. The sidecar comes from the upstream `daprio/daprd` image at deploy time — standard Dapr pattern, keeps the image minimal.

## Agent internals

### Persona

`MSAgentFramework.Agent.Contracts/Persona.cs` holds the `dotnet-csharp-expert` system prompt as a single `const string`. The prompt covers:

- Idiomatic modern C# (C# 13+: records, primary constructors, pattern matching, collection expressions, required members, file-scoped namespaces).
- Async/await rules, `IAsyncEnumerable<T>`, `CancellationToken` discipline.
- LINQ, generics, NRT, `Span<T>`/`Memory<T>`, `ref struct`, performance idioms.
- BCL guidance (`System.Text.Json`, `IOptions<T>`, `ILogger<T>`, `IHostedService`).
- ASP.NET Core minimal APIs, EF Core query patterns.
- xUnit + `Microsoft.Extensions.Hosting` test patterns.
- Response format: short, code-first, references C# language version when relevant. **Advisory only** — the agent does not act on any filesystem.

This persona is standalone. It is not a mirror of any other artifact in the repo.

### Composition (`MSAgentFramework.Agent/Program.cs`)

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddDaprAiConversation();
builder.Services.AddSingleton<IChatClient, DaprChatClient>();
builder.Services.AddSingleton<AIAgent>(sp =>
    new ChatClientAgent(
        sp.GetRequiredService<IChatClient>(),
        instructions: Persona.DotnetCsharpExpert,
        name: "dotnet-csharp-expert"));
builder.Services.AddSingleton<ChatMessageStore, DaprStateChatMessageStore>();

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();
app.MapDefaultEndpoints();        // /health, /alive
app.MapMcp("/mcp");               // → http://host:8085/mcp
app.Run();
```

### `DaprChatClient : IChatClient`

~50 LOC. Translates `IList<ChatMessage>` → `ConversationInput[]`, calls `DaprConversationClient.ConverseAsync("anthropic-llm", inputs, cancellationToken)`, maps the response back to `ChatResponse`. Streaming returns a single chunk async-enumerable until Dapr.AI exposes streaming on the Anthropic component — degrade gracefully, do not throw.

### MCP tool surface

One tool only:

```csharp
[McpServerToolType]
public sealed class CsharpExpertTool(AIAgent agent, IThreadStore threads)
{
    [McpServerTool, Description("Ask the .NET / C# expert for advice on idiomatic C#, " +
        "BCL usage, async patterns, LINQ, EF Core, ASP.NET Core, performance, or xUnit testing.")]
    public async Task<AskResult> AskDotnetCsharpExpert(
        [Description("The question, including code snippets if relevant.")] string question,
        [Description("Optional thread id to continue a prior conversation.")] string? threadId = null,
        CancellationToken ct = default)
    {
        var thread = await threads.LoadOrCreateAsync(threadId, ct);
        var run = await agent.RunAsync(question, thread, cancellationToken: ct);
        await threads.SaveAsync(thread, ct);
        return new AskResult(thread.Id, run.Text);
    }
}
public record AskResult(string ThreadId, string Answer);
```

The agent is **advisory-only**. No `Read`, `Write`, `Edit`, `Bash`, etc. tools are exposed on the remote side — the calling Claude Code already has those, and the remote agent's job is to think, not to act on the caller's filesystem.

### Memory

Threads persist to Redis through Dapr state.

- `DaprStateChatMessageStore : ChatMessageStore` reads/writes the thread's serialized messages keyed by `agent-thread:{threadId}` against the `agent-memory` state store.
- Threadless calls (`threadId` omitted) get a freshly-generated `Guid` ID, returned in the response so the caller can continue.
- TTL of 7 days set via Dapr state metadata to prevent unbounded growth.

## Dapr components

```yaml
# deploy/dapr/components/anthropic-llm.yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata: { name: anthropic-llm }
spec:
  type: conversation.anthropic
  version: v1
  metadata:
    - { name: key,      secretKeyRef: { name: ANTHROPIC_API_KEY, key: ANTHROPIC_API_KEY } }
    - { name: model,    value: "claude-opus-4-7" }
    - { name: cacheTTL, value: "10m" }
auth: { secretStore: envvar-secretstore }
```

```yaml
# deploy/dapr/components/agent-memory.yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata: { name: agent-memory }
spec:
  type: state.redis
  version: v1
  metadata:
    - { name: redisHost,       value: "localhost:6379" }
    - { name: keyPrefix,       value: "agent" }
    - { name: actorStateStore, value: "true" }
```

```yaml
# deploy/dapr/components/envvar-secretstore.yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata: { name: envvar-secretstore }
spec:
  type: secretstores.local.env
  version: v1
```

## AppHost (Aspire) wiring

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var redis = builder.AddRedis("redis").WithDataVolume();

var agent = builder.AddProject<Projects.MSAgentFramework_Agent>("agent")
    .WithReference(redis)
    .WithDaprSidecar(new DaprSidecarOptions
    {
        AppId = "dotnet-csharp-agent",
        AppPort = 8085,
        ResourcesPaths = ["../../deploy/dapr/components"]
    });

builder.Build().Run();
```

## Dockerfile (sketch)

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY MSAgentFramework.sln ./
COPY src/ ./src/
RUN dotnet publish src/MSAgentFramework.Agent -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_URLS=http://+:8085
EXPOSE 8085
USER $APP_UID
ENTRYPOINT ["dotnet", "MSAgentFramework.Agent.dll"]
```

## Claude Code registration

One-time, on any machine that should call this remote agent:

```bash
claude mcp add --transport http --scope user dotnet-csharp-expert \
  http://<deployed-host>:8085/mcp
```

After registration, any Claude Code session can invoke the `AskDotnetCsharpExpert` tool. That is what "callable as a remote subagent" means in practice for MCP.

## Tests

Two minimal slices in `MSAgentFramework.Agent.Tests`:

1. **Adapter test** — `DaprChatClient` round-trips a stubbed `DaprConversationClient`: verifies role mapping, message ordering, error propagation.
2. **End-to-end MCP test** — `WebApplicationFactory<Program>` boots the agent against a fake `IChatClient`. Calls `tools/call` over MCP, asserts the response shape and that the thread was persisted to a stubbed Dapr state store.

## Out of scope for this design

- A `kagent/` analog (third Kubernetes mirror) — can be added later as a separate Agent CRD pointing at the same MCP endpoint.
- Streaming MCP responses — current Dapr.AI does not surface streaming; revisit when it does.
- Multi-agent orchestration (handoff/group-chat) — the agent is a single `ChatClientAgent` for now.
- Authn/authz on the MCP endpoint — assumed network-restricted; bearer-token middleware can be added once a deployment target is chosen.

## Open follow-ups

- Confirm `Aspire.Hosting.Dapr` API surface against the .NET 10 / Aspire 10.x release at implementation time — the Dapr Aspire integration has historically lived in a community package and the namespace has shifted.
- Confirm `Dapr.AI` package version supports Anthropic with `model: claude-opus-4-7` exactly (model id), versus a near-name like `claude-opus-4-7-latest`.
