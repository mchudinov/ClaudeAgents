# MSAgentFramework
MSAgentFramework is a .NET 10 solution that hosts an MCP tool-backed advisory agent for modern C#/.NET guidance. The runtime uses .NET Aspire for orchestration, Dapr for session persistence, Anthropic for model responses, and optional Context7 MCP tools for live library documentation lookup.

## Architecture at a glance
- **AppHost (`src/MSAgentFramework.AppHost`)**
  - Starts the distributed app (agent + Dapr sidecar + RedisInsight container).
  - Reads `ANTHROPIC_API_KEY` (required) and `CONTEXT7_API_KEY` (optional) from config/environment.
  - Configures Dapr sidecar resources path: `../../../deploy/dapr/components`.
- **Agent service (`src/MSAgentFramework.Agent`)**
  - ASP.NET Core service exposing an MCP endpoint at `/mcp`.
  - Registers MCP tools from assembly.
  - Builds a `ChatClientAgent` using `Anthropic.SDK` (`claude-opus-4-7` by default, override via `ANTHROPIC_MODEL`).
  - Adds optional Context7 tools when `CONTEXT7_API_KEY` is available.
- **MCP tool (`src/MSAgentFramework.Agent/Mcp/CsharpExpertTool.cs`)**
  - Exposes `ask_dotnet_csharp_expert`.
  - Accepts a question and optional thread id.
  - Returns `AskResult { ThreadId, Answer }`.
- **Contracts (`src/MSAgentFramework.Agent.Contracts`)**
  - Shared DTOs and persona loader.
  - Persona instructions come from embedded markdown resource: `dotnet-csharp-expert.md`.
- **Session store (`src/MSAgentFramework.Agent/Sessions`)**
  - Persists agent sessions in Dapr state store `agent-memory`.
  - Key prefix: `agent-thread:`
  - Session TTL: 7 days.
- **Service defaults (`src/MSAgentFramework.ServiceDefaults`)**
  - Aspire defaults (health checks, service discovery, resilience, OpenTelemetry).
- **Tests (`tests/MSAgentFramework.Agent.Tests`)**
  - xUnit tests for persona constraints and Dapr session store behavior.

## Solution layout
```text
MSAgentFramework.slnx
src/
  MSAgentFramework.Agent.Contracts/
  MSAgentFramework.Agent/
  MSAgentFramework.AppHost/
  MSAgentFramework.ServiceDefaults/
tests/
  MSAgentFramework.Agent.Tests/
```

## Prerequisites
- .NET SDK 10.x
- Dapr CLI/runtime available locally
- Docker Desktop (used by Aspire resources like RedisInsight)
- Anthropic API key
- (Optional) Context7 API key

## Required configuration
The AppHost fails fast if `ANTHROPIC_API_KEY` is not set.

### Option A: user-secrets (recommended for local dev)
From the repo root:
```powershell
dotnet user-secrets set "ANTHROPIC_API_KEY" "<your-key>" --project .\src\MSAgentFramework.AppHost\MSAgentFramework.AppHost.csproj
dotnet user-secrets set "CONTEXT7_API_KEY" "<your-context7-key>" --project .\src\MSAgentFramework.AppHost\MSAgentFramework.AppHost.csproj
```

### Option B: environment variables
```powershell
$env:ANTHROPIC_API_KEY = "<your-key>"
$env:CONTEXT7_API_KEY = "<your-context7-key>"   # optional
```

## Dapr components
`AppHost.cs` points Dapr to:
- `deploy/dapr/components` (relative to the app host project)

This repository currently does not include those component manifests, so create them before running. At minimum you need a state-store component compatible with store name:
- `agent-memory`

The comments in `AppHost.cs` also indicate an env-var secret store setup may be expected.

## Running the app
From repo root:
```powershell
dotnet run --project .\src\MSAgentFramework.AppHost\MSAgentFramework.AppHost.csproj
```

Useful local endpoints:
- AppHost dashboard URLs are assigned by launch profile (`src/MSAgentFramework.AppHost/Properties/launchSettings.json`).
- Agent service default local URL is `http://localhost:8085` (`src/MSAgentFramework.Agent/Properties/launchSettings.json`).
- MCP endpoint path is `/mcp`.

## MCP usage
The primary MCP tool is:
- `ask_dotnet_csharp_expert(question, threadId?)`

Behavior:
- If `threadId` is omitted, a new session is created and returned.
- If `threadId` is provided, conversation state is loaded from Dapr and continued.
- The result contains:
  - `ThreadId`: current conversation id
  - `Answer`: generated advisory response

## Running tests
From repo root:
```powershell
dotnet test .\tests\MSAgentFramework.Agent.Tests\MSAgentFramework.Agent.Tests.csproj
```

## Notes
- The agent is intentionally advisory-only by persona design (no direct filesystem/shell execution).
- Context7 integration is optional; the service still runs if it cannot connect.
