# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this directory is

`MSAgentFrameworkFoundry/` is **greenfield**. The solution file is literally empty:

```xml
<Solution />
```

There is no source code yet — only `docs/idea.md` (the spec) and the empty `.slnx`. Do **not** assume any project layout, NuGet packages, or build commands exist; verify before referencing them.

Scope per `docs/idea.md`: build **two cooperating agents** — a .NET 10 C# *Developer* and a C# *Code Reviewer* — using the **Microsoft Agent Framework** (`Microsoft.Agents.AI`) in C#, backed by **Anthropic `claude-opus-4-7`**, deployed to **Azure AI Foundry**. This is the Foundry-hosted counterpart to the sibling `../MSAgentFrameworkDapr/` project, which implements a similar advisory-agent idea on Aspire + Dapr.

## Read these before implementing anything

1. **`docs/idea.md`** — the canonical requirements. Re-read it; the workflow it specifies is load-bearing:
   - User invokes the Developer agent from the Claude Code console.
   - Developer clones the target GitHub repo, implements the change, opens a PR, then **calls the Code Reviewer agent**.
   - Code Reviewer reviews the PR, approves it, then **calls back into the Developer agent** to notify approval.
   - Developer then runs `/compact` on its own context.
2. **`../MSAgentFrameworkDapr/README.md`** and `../MSAgentFrameworkDapr/src/` — structural reference for: `.slnx` + `src/` + `tests/` layout, `ChatClientAgent` wiring with `Anthropic.SDK`, MCP tool registration, persona-as-embedded-markdown pattern, and the session-store abstraction. Use these patterns unless the Foundry deployment requires deviating (e.g. memory store will be Foundry-native, not Dapr).
3. **`../CLAUDE.md`** — repo-wide context (Docker dev container, kagent flavor, port 8081 convention). The port-8081 rule applies only to the Docker dev shell, not to this subproject's runtime, which targets Azure AI Foundry.

## Development methodology

- **Use TDD** (test-driven development) as the software development methodology: write a failing test, make it pass with the minimum change, then refactor. Add tests before production code, not after.
- **Always use the context7 MCP server** for any development documentation search (libraries, frameworks, SDKs, APIs, CLI tools). Prefer it over web search and over relying on training-data recall, even for well-known libraries — versions and APIs drift.

## Non-negotiable requirements from `docs/idea.md`

- **Model:** Anthropic `claude-opus-4-7` for both agents.
- **Framework:** Microsoft Agent Framework, C#, .NET 10.
- **MCP:** Both agents must have the **GitHub MCP server** wired up (this can be provided by the Azure AI Foundry project rather than self-hosted).
- **Memory:** Both agents must have persistent memory.
- **Persona files:** Each agent's role description lives in its own markdown file (mirror the Dapr project's embedded-resource pattern in `MSAgentFramework.Agent.Contracts`).
- **`/effort` is a configuration option**, not hard-coded:
  - Developer agent default: `xhigh`
  - Code Reviewer agent default: `high`
- **Inter-agent calls** are explicit handoffs (Developer→Reviewer→Developer), so design the agents so each can invoke the other (likely via tool calls / agent-as-tool, with the PR number as the correlation token).

## Sibling Dapr project as a reference, not a template to copy

`MSAgentFrameworkDapr/` is **advisory-only** (no filesystem/shell), runs on **Aspire + Dapr**, persists sessions in a `agent-memory` Dapr state store, and exposes one MCP tool (`ask_dotnet_csharp_expert`). The Foundry project is different in three ways that will shape the architecture:

1. **Agentic, not advisory** — the Developer must clone repos, edit code, push branches, open PRs. Plan for filesystem/Git capability (likely via additional MCP servers or platform-provided tools), not the advisory persona pattern.
2. **Two agents that call each other**, not a single tool-exposing agent.
3. **Azure AI Foundry deployment**, so memory and MCP should use Foundry-native facilities where available rather than Dapr components.

## Known stale parent doc

`../CLAUDE.md` says ``MSAgentFramework/` is currently an empty placeholder.`` That sentence predates the split into `MSAgentFrameworkDapr/` and `MSAgentFrameworkFoundry/`. When you change either subproject's structure, update the parent CLAUDE.md to match — they describe the same repo.

## Build / test / run

There are no build, test, or lint commands yet — `MSAgentFrameworkFoundry.slnx` contains zero projects. Once projects are added, expect to run them with the standard `dotnet build` / `dotnet test` / `dotnet run --project <path>` against the `.slnx`. Do not invent commands before the corresponding `.csproj` files exist.
