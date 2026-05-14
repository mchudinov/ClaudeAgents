# Foundry Developer + Code Reviewer Agents — Design

**Date:** 2026-05-13
**Status:** Approved for implementation planning
**Scope:** `MSAgentFrameworkFoundry/` subproject

## 1. Goal

Build two cooperating agents — a .NET 10 C# **Developer** and a C# **Code Reviewer** — using the **Microsoft Agent Framework** (`Microsoft.Agents.AI`) in C# against **Anthropic `claude-opus-4-7`** hosted on **Azure AI Foundry**, deployed as two separate **Azure Container Apps**.

The user invokes the Developer from the Claude Code console with a programming task and a target GitHub repository. The Developer clones the repo, implements the change, runs `dotnet build` and `dotnet test`, pushes a branch, opens a pull request, then calls the Reviewer over MCP. The Reviewer reads the PR via GitHub MCP and returns a verdict. On approval the Developer compacts its conversation thread and returns the PR URL to Claude Code. On change requests the Developer iterates up to a bounded retry cap and re-invokes the Reviewer.

This document is the architectural spec. It does **not** prescribe implementation order — that belongs to the implementation plan.

## 2. Locked-in decisions

| # | Decision |
|---|---|
| D-1 | **Two agents in two separate ACA apps**; MCP-over-HTTP between them; one solution |
| D-2 | **Foundry hosts**: the Claude Opus 4.7 model deployment and the GitHub MCP server |
| D-3 | **`IChatClient` abstraction** — both agents use `ChatClientAgent` from `Microsoft.Agents.AI` against the Foundry-deployed model |
| D-4 | **Developer container has `git` and the .NET 10 SDK at runtime**; Reviewer is review-only (no git, no SDK) |
| D-5 | **Synchronous handoff** — Developer calls Reviewer over MCP and waits for the verdict |
| D-6 | **Bounded iteration** — up to 3 review rounds by default, configurable per service (`MaxReviewRounds`) |
| D-7 | **Per-thread persisted memory** in Azure Cosmos DB; no cross-thread or semantic memory |
| D-8 | **Personas as embedded markdown resources** packed into each agent project |
| D-9 | **`/effort` enum** (`Low|Medium|High|Xhigh|Max`) mapped to thinking-budget + max-output tokens; default Developer=`Xhigh`, Reviewer=`High`; per-invocation override |
| D-10 | **`/compact` on Approved** — replace Developer thread history with an LLM-generated ≤500-token summary |
| D-11 | **Reviewer-unreachable retry** — 1 initial attempt + 2 auto-retries with exponential backoff (cap 30 s); on exhaustion return `Error` with the PR URL so a human can re-invoke |
| D-12 | **Aspire AppHost is dev-only** (`IsPackable=false`); production infrastructure is **hand-written Bicep**, sole source of truth |
| D-13 | **Single production ACA environment** for v1 — no dev/staging split |
| D-14 | **Serilog** owns logs, configured **exclusively** from `appsettings.json` (`Serilog:*` section), **Console sink only** (JSON formatter). **OpenTelemetry** owns traces and metrics |
| D-15 | **Identity**: system-assigned managed identity per ACA app; `DefaultAzureCredential` throughout; **no static API keys in code or env vars** — GitHub PAT lives in Key Vault and is mounted as an ACA secret |
| D-16 | **HTTP port 8081** for both containers, matching the repo CLAUDE.md convention |
| D-17 | **Reviewer never merges, never proposes patches** — approves only; comments describe what's wrong, Developer fixes |

## 3. Solution layout

```
MSAgentFrameworkFoundry.slnx
src/
  Foundry.Agents.Contracts/        # DTOs, persona loader, EffortLevel enum, EffortBudget table, MCP request/result types
  Foundry.Agents.Memory/           # ICosmosThreadStore + impl, ChatMessage <-> Cosmos document mapping, thread compactor
  Foundry.Agents.ServiceDefaults/  # Aspire-style defaults: health checks, OpenTelemetry, problem details, service discovery, Serilog binding
  Foundry.Agents.Developer/        # ASP.NET Core; MCP server at /mcp; tool: assign_dotnet_task; GitWorkspace; ReviewerMcpClient
  Foundry.Agents.Reviewer/         # ASP.NET Core; MCP server at /mcp; tool: review_pull_request; GitHub MCP client only
  Foundry.Agents.AppHost/          # Aspire AppHost — local-dev orchestration ONLY; IsPackable=false; never deployed
tests/
  Foundry.Agents.Memory.Tests/
  Foundry.Agents.Developer.Tests/
  Foundry.Agents.Reviewer.Tests/
  Foundry.Agents.Developer.IntegrationTests/   # E2E against AppHost, gated on RUN_E2E_TESTS=1
  Foundry.Agents.TestUtils/                    # shared fakes (IGitWorkspace, IChatClient, clock, id generator)
infra/
  main.bicep
  modules/
    cosmos.bicep
    keyvault.bicep
    foundry.bicep
    containerapps.bicep
    loganalytics.bicep
    acr.bicep
personas/
  developer.md                     # embedded into Developer.csproj via <EmbeddedResource>
  reviewer.md                      # embedded into Reviewer.csproj via <EmbeddedResource>
docs/
  idea.md                          # original requirements (existing)
  superpowers/specs/2026-05-13-foundry-agents-design.md  # this document
.github/workflows/
  pr.yml                           # restore, build, test
  deploy.yml                       # build/push images, az containerapp update × 2
```

Naming note: the prefix `Foundry.Agents.*` deliberately diverges from the sibling Dapr project's `MSAgentFramework.*` so the two are distinguishable in IDE tabs and `dotnet` output. The solution filename `MSAgentFrameworkFoundry.slnx` is preserved.

## 4. Runtime architecture & data flow

### 4.1 Topology

```
Claude Code (laptop)
       │  MCP/HTTP   assign_dotnet_task(repo, task, threadId?, effort?)
       ▼
┌──────────────────────────────┐                 ┌──────────────────────────────┐
│ Developer ACA app            │ ── MCP/HTTP ──▶ │ Reviewer ACA app             │
│ - .NET 10 SDK + git          │ review_pull_    │ - chiseled aspnet runtime    │
│ - GitWorkspace service       │   request       │ - GitHub MCP client          │
│ - ReviewerMcpClient          │ ◀── result ──── │ - no git, no SDK             │
│ - ChatClientAgent(Foundry)   │                 │ - ChatClientAgent(Foundry)   │
└──────────────────────────────┘                 └──────────────────────────────┘
       │       ▲                                          │       ▲
       │ R/W   │                                          │ R/W   │
       ▼       │                                          ▼       │
┌──────────────────────────────────────────────────────────────────┐
│ Azure Cosmos DB — db `agentdb`, container `agent-threads`        │
│ PK=/agentRole, default TTL 604 800 s (7d)                        │
└──────────────────────────────────────────────────────────────────┘

Both apps call:
  - Foundry-hosted Claude Opus 4.7 (chat completions)
  - Foundry-hosted GitHub MCP server (separate MCP endpoint within the Foundry project)
```

### 4.2 Developer MCP surface (entry point)

```csharp
public sealed record AssignTaskRequest(
    string GithubRepo,            // "owner/repo"
    string TaskDescription,
    string? ThreadId,             // null => new thread
    EffortLevel? Effort);         // null => app-config default (Xhigh)

public sealed record AssignTaskResult(
    string ThreadId,
    AssignTaskStatus Status,
    string? PrUrl,                // null on early failure (clone/build/test)
    string? Summary,              // assistant's final summary
    IReadOnlyList<string> UnresolvedComments);  // populated on ReviewFailed

public enum AssignTaskStatus
{
    Approved,        // PR opened, Reviewer approved, thread compacted
    ReviewFailed,    // PR opened, hit MaxReviewRounds without approval
    BuildFailed,     // dotnet build or dotnet test failed; no PR opened
    Error            // unrecoverable (clone, push, MCP, model error)
}
```

`assign_dotnet_task` is the only externally-exposed tool. Internal endpoints exist for health (`/health/live`, `/health/ready`) via `MapDefaultEndpoints()`.

### 4.3 Reviewer MCP surface (Developer-only)

```csharp
public sealed record ReviewRequest(
    string GithubRepo,
    int PrNumber,
    string? ThreadId,             // reviewThreadId; null on first round
    EffortLevel? Effort);

public sealed record ReviewResult(
    string ThreadId,              // reviewThreadId, returned for reuse
    ReviewVerdict Verdict,
    IReadOnlyList<ReviewComment> Comments,
    string Summary);

public enum ReviewVerdict
{
    Approved,
    ChangesRequested,
    RejectedBlocking              // refuse to iterate (malicious/out-of-scope)
}

public sealed record ReviewComment(string? FilePath, int? Line, string Body);
```

Reviewer's ingress is **internal-only** (ACA `external: false`); only the Developer app inside the same ACA environment can reach it. Claude Code cannot call Reviewer directly.

### 4.4 End-to-end sequence

```
1.  Claude Code  ──assign_dotnet_task(repo, task, threadId?)──▶  Developer
2.  Developer    ── ICosmosThreadStore.LoadAsync(threadId, "developer")  (or create new with persona)
3.  Developer    ── git clone https://x-access-token:$PAT@github.com/<repo>  into /work/<threadId>
4.  Developer    ── ChatClientAgent.RunAsync(task)
                    # tools available: file ops, dotnet build, dotnet test, GitHub MCP (read-only)
5.  Developer    ── dotnet build && dotnet test           # fail-closed before opening PR
6.  Developer    ── git checkout -b agent/<thread-short>
                 ── git add . && git commit && git push
                 ── GitHub MCP: create_pull_request          ▶ PrNumber
7.  Developer    ── reviewRound = 1 (persisted to thread before call)
                 ── ReviewerMcpClient.review_pull_request(repo, PrNumber, reviewThreadId?, effort)
                    # 1 initial + 2 retries with exponential backoff (cap 30s) on transient HTTP failure
                 ──────────────────────────────────────────────▶  Reviewer
8.  Reviewer     ── ICosmosThreadStore.LoadAsync(threadId, "reviewer")  (create with persona if null)
9.  Reviewer     ── ChatClientAgent.RunAsync
                    # tools: GitHub MCP — get_pull_request_diff, get_pull_request_files,
                    #        create_pending_review, add_comment_to_pending_review,
                    #        pull_request_review_write (submit_review with APPROVE / REQUEST_CHANGES)
10. Reviewer     ── persist thread, return ReviewResult
11. Developer    ── if Approved:    /compact thread (LLM-summarize, replace history), persist
                     return AssignTaskResult.Approved(PrUrl, Summary)
                  ── if ChangesRequested && reviewRound < MaxReviewRounds:
                     ChatClientAgent.RunAsync("address these comments: ...")
                     commit + push amend; reviewRound++; goto step 7 with same reviewThreadId
                  ── if ChangesRequested && reviewRound >= MaxReviewRounds OR RejectedBlocking:
                     return AssignTaskResult.ReviewFailed(PrUrl, Summary, Comments)
```

### 4.5 Workspace lifecycle

ACA scales to zero. The workspace at `/work/<threadId>` is **ephemeral** — a fresh `dotnet build` container has nothing in it.

**Rule:** every invocation re-clones from the remote. If the agent branch already exists remote-side (resume case), the clone uses `-b agent/<thread-short>` to pick up the prior work. The clone is the only source of truth between invocations; nothing on the container disk is relied upon.

`/work` is an emptyDir-style ACA volume — sized for one large repo (5 GB), wiped on pod restart.

### 4.6 Resume semantics

When `assign_dotnet_task` is called with a `threadId` whose thread already has `prNumber` set, the Developer treats the call as a **resume** rather than a new task:

- If `TaskDescription` is empty or matches a "retry-review" sentinel, Developer **skips steps 4–6** (no model call, no edits, no new commits) and jumps to step 7 with the existing `prNumber` and `reviewThreadId`. This is the path for "Reviewer unreachable" recovery.
- If `TaskDescription` is a new instruction, Developer treats it as a follow-up turn against the existing PR — re-clones the branch, runs the model with the new instruction in the existing thread, commits + pushes, then proceeds to step 7. `reviewRound` resets to `1` only if the previous outcome was `Approved` (a new round of changes on a previously-approved PR is a new review); otherwise it continues from the recorded value.

Resume does **not** create a second PR. One thread, one PR, for the thread's lifetime.

## 5. Memory, persona, /effort, /compact

### 5.1 Thread store

Single Cosmos container `agent-threads`, partitioned by `/agentRole` (`developer` | `reviewer`). One document per thread:

```jsonc
{
  "id": "01J6Z8K...",                  // threadId (ULID)
  "agentRole": "developer",            // partition key
  "createdUtc": "2026-05-13T...",
  "updatedUtc": "2026-05-13T...",
  "ttl": 604800,                       // refreshed on each write
  "linkedReviewThreadId": "01J6...",   // developer thread → its reviewer thread; null on reviewer side
  "githubRepo": "owner/repo",
  "prNumber": 142,
  "reviewRound": 1,
  "effort": "Xhigh",
  "messages": [
    { "role": "system",    "content": "<persona markdown>" },
    { "role": "user",      "content": "add retry to OrderService" },
    { "role": "assistant", "content": "..." }
  ],
  "summary": null,                     // populated after /compact
  "_etag": "..."                       // optimistic concurrency
}
```

`ICosmosThreadStore` (in `Foundry.Agents.Memory`):

```csharp
Task<AgentThread> LoadOrCreateAsync(string threadId, AgentRole role, string personaMarkdown, CancellationToken ct);
Task SaveAsync(AgentThread thread, CancellationToken ct);           // upsert with ETag
Task CompactAsync(string threadId, AgentRole role, string summary, CancellationToken ct);
```

Concurrency: ETag-based optimistic concurrency. A 412 on save means another invocation is mid-flight against the same `threadId` — the agent service rejects the second call with a 409 to its caller (Developer or Reviewer). Concurrent calls against the same `threadId` are a caller bug, not a normal case.

### 5.2 Persona

- `personas/developer.md` and `personas/reviewer.md` are checked into the repo root, embedded into each agent project as a resource:
  ```xml
  <ItemGroup>
    <EmbeddedResource Include="..\..\personas\developer.md" LogicalName="persona.md" />
  </ItemGroup>
  ```
- `PersonaLoader.LoadAsync(role)` reads the `persona.md` resource stream and returns the markdown verbatim.
- The persona is prepended as the **first system message** when a thread is created. On thread resume, `messages[0]` already holds the persona — it is **not** re-injected. A thread's persona is frozen at creation.

Persona content is out of scope for this design, but two non-negotiable constraints:

- **Developer persona** must instruct: ".NET 10 C# developer; always runs `dotnet build` and `dotnet test` before pushing; never pushes to default branch; one PR per task; never merges PRs."
- **Reviewer persona** must instruct: "C# code reviewer; approve via GitHub MCP's `pull_request_review_write` only; **never** call any merge tool; comments describe what's wrong, do not propose patches; verdicts limited to `Approved | ChangesRequested | RejectedBlocking`."

### 5.3 `/effort` configuration

```csharp
public enum EffortLevel { Low, Medium, High, Xhigh, Max }
```

`EffortBudget.cs` in `Foundry.Agents.Contracts`:

| Level   | thinkingBudgetTokens | maxOutputTokens |
|---------|----------------------|-----------------|
| Low     | 1 024                | 4 096           |
| Medium  | 4 096                | 8 192           |
| High    | 16 384               | 16 384          |
| Xhigh   | 32 768               | 32 768          |
| Max     | 64 000               | 64 000          |

These are tunable constants — a starting table, not load-bearing values.

**Resolution order per invocation:**

1. `Effort` field on the MCP request (highest priority)
2. `EffortLevel` recorded on the thread (resume case)
3. App-config default (`DEVELOPER_DEFAULT_EFFORT=Xhigh`, `REVIEWER_DEFAULT_EFFORT=High` via ACA env vars)
4. Hard-coded fallback (`High`)

The resolved level + budgets feed into `ChatOptions.AdditionalProperties["thinking"] = new { type = "enabled", budget_tokens = ... }` and `ChatOptions.MaxOutputTokens = ...` when calling the Foundry-deployed Claude endpoint. The exact `IChatClient` package is selected at implementation time depending on which Foundry inference API surface is available (`Microsoft.Extensions.AI.AzureAIInference` is the current best guess); if Foundry's Anthropic surface differs, the wrapper is local to one file per service.

### 5.4 `/compact`

Runs only on the Developer side, only on `AssignTaskStatus.Approved`. Steps:

1. Call the model with a fixed prompt:
   > "Summarize this conversation in ≤500 tokens, preserving: the original task, the final PR URL, key technical decisions, and any commitments to follow up. Output prose, not bullets."
2. Replace `thread.messages` with:
   ```
   [ system(persona),
     system("Prior context summary: <summary>"),
     assistant("<summary>") ]
   ```
3. Store the raw summary in `thread.summary` for audit.
4. Persist the compacted thread.

The Reviewer thread is **not** compacted automatically — review threads are short-lived per PR and cheap to keep whole. Revisit only if review threads grow large in practice.

## 6. Error handling, iteration, observability

### 6.1 Developer failure taxonomy

| Failure | Detection | Returned status | Side effects |
|---|---|---|---|
| Clone fails (auth, repo not found) | `git clone` non-zero exit | `Error` | Nothing pushed; thread records the diagnostic |
| `dotnet build` fails | Process exit code | `BuildFailed` | Branch may exist locally; **not** pushed; no PR opened |
| `dotnet test` fails | Process exit code | `BuildFailed` | Same — fail-closed before opening a PR |
| `git push` fails | non-zero exit | `Error` | Commits local only; thread records diagnostic |
| `create_pull_request` MCP fails | MCP error envelope | `Error` | Branch pushed but no PR; `Summary` includes the branch name so a human can open the PR manually |
| Reviewer unreachable | HTTP timeout / 5xx after 1+2 retries (exponential backoff, cap 30 s) | `Error` | PR is open; thread records "review pending — Reviewer unreachable". Human re-invokes by calling `assign_dotnet_task` again with the same `threadId` — Developer detects the existing `prNumber` on the thread and resumes at step 7 (Reviewer call) without re-running the model. See §4.6. |
| Reviewer returns `RejectedBlocking` | Verdict from Reviewer | `ReviewFailed` | No iteration; surfaced immediately |
| Iteration cap exceeded (`ChangesRequested` × `MaxReviewRounds`) | Local counter on thread | `ReviewFailed` | All comments returned; PR remains open for a human |
| Anthropic/Foundry model error | `IChatClient` throws | Wrapped, retried twice with backoff, then `Error` | Diagnostic logged with `threadId` |
| Cosmos write fails | SDK exception | `Error`, **don't** swallow | Thread state may be partial — next resume re-reads and reconciles via ETag |

### 6.2 Reviewer failure modes

Reviewer is intentionally simpler: GitHub MCP unavailable → Reviewer returns HTTP 503 to Developer (not a `ReviewResult`); Developer treats it as "Reviewer unreachable" and applies its retry policy. Reviewer never produces a `ReviewResult` it isn't confident in — there is no "I don't know" verdict.

### 6.3 Idempotency

The Developer thread holds the canonical `reviewRound` counter. It is incremented and persisted **before** the Reviewer call. So a crash between increment and call resumes counting from the right place. The same `reviewThreadId` is reused across rounds, so Reviewer's context grows monotonically and it can verify the change addresses what it asked for last round.

### 6.4 Observability

- **Logs — Serilog only.**
  - Configured entirely from `appsettings.json` `Serilog:*` section. **No programmatic Serilog config** beyond the single binding line in `Program.cs`:
    ```csharp
    builder.Host.UseSerilog((ctx, services, cfg) =>
        cfg.ReadFrom.Configuration(ctx.Configuration).ReadFrom.Services(services));
    ```
  - **Console sink only**, JSON formatter, structured properties.
  - `Enrich.FromLogContext` so OTel `TraceId`/`SpanId` flow into every log entry.
  - Example `appsettings.json` block (each agent has its own):
    ```json
    {
      "Serilog": {
        "MinimumLevel": {
          "Default": "Information",
          "Override": {
            "Microsoft.AspNetCore": "Warning",
            "Microsoft.Hosting.Lifetime": "Information",
            "System.Net.Http": "Warning"
          }
        },
        "WriteTo": [
          { "Name": "Console", "Args": { "formatter": "Serilog.Formatting.Json.JsonFormatter, Serilog" } }
        ],
        "Enrich": [ "FromLogContext", "WithMachineName" ],
        "Properties": { "Application": "Foundry.Agents.Developer" }
      }
    }
    ```
- **Traces & metrics — OpenTelemetry**, wired in `Foundry.Agents.ServiceDefaults` via `builder.AddServiceDefaults()`. OTLP export to the ACA-platform-provided endpoint (Log Analytics).
- **Span attributes** on every span: `agent.role` (`developer` | `reviewer`), `agent.thread_id`, `agent.review_thread_id` (Developer side only), `agent.review_round`, `github.repo`, `github.pr_number` (once known), `effort.level`.
- **Key spans:** `assign_task`, `git_clone`, `dotnet_build`, `dotnet_test`, `git_push`, `mcp.github.create_pr`, `mcp.reviewer.review`, `agent.compact`.
- **Errors** recorded with `exception.type` / `exception.message` and the `threadId` so a failed run is replayable.
- **No alerting or dashboards in v1.**

## 7. Deployment & infrastructure

### 7.1 Local development (Aspire AppHost, dev-only)

```powershell
dotnet user-secrets set "Parameters:anthropic-foundry-endpoint" "https://..." --project src/Foundry.Agents.AppHost
dotnet user-secrets set "Parameters:github-mcp-endpoint" "https://..." --project src/Foundry.Agents.AppHost
dotnet user-secrets set "Parameters:github-pat" "ghp_..."                       --project src/Foundry.Agents.AppHost
dotnet run --project src/Foundry.Agents.AppHost
```

The AppHost wires:

- `AddAzureCosmosDB("threads").RunAsEmulator()` — Cosmos linux emulator container
- `AddProject<Projects.Foundry_Agents_Developer>("developer")` and `AddProject<Projects.Foundry_Agents_Reviewer>("reviewer")` — service discovery injects the Reviewer URL into Developer
- `AddParameter("anthropic-foundry-endpoint")`, `AddParameter("github-mcp-endpoint")`, `AddParameter("github-pat", secret: true)`

**No local GitHub MCP container** — both dev and prod hit the Foundry-hosted GitHub MCP endpoint. The Foundry endpoint is reachable from a laptop with a valid token.

Claude Code is pointed at the local Developer's `/mcp` URL Aspire prints (`claude mcp add foundry-dev http://localhost:<port>/mcp`).

`Foundry.Agents.AppHost.csproj` is marked `<IsPackable>false</IsPackable>` — never deployed, never used at deploy time, never emits a manifest.

### 7.2 Production — hand-written Bicep

`infra/main.bicep` is the **sole source of truth** for Azure-side infrastructure. Modules:

| Module | Resource(s) |
|---|---|
| `loganalytics.bicep` | `Microsoft.OperationalInsights/workspaces` — receives ACA logs + OTel exports |
| `acr.bicep` | `Microsoft.ContainerRegistry/registries` — Premium, AcrPull granted to ACA identities |
| `cosmos.bicep` | `Microsoft.DocumentDB/databaseAccounts` serverless, single region; database `agentdb`; container `agent-threads` (PK `/agentRole`, default TTL 604 800 s) |
| `keyvault.bicep` | `Microsoft.KeyVault/vaults`; holds `github-pat` secret |
| `foundry.bicep` | `Microsoft.MachineLearningServices/workspaces` (Foundry hub) + project; Claude Opus 4.7 model deployment; GitHub MCP server resource |
| `containerapps.bicep` | `Microsoft.App/managedEnvironments` + two `Microsoft.App/containerApps`: `developer-app` (external HTTPS ingress, port 8081, `transport: http`) and `reviewer-app` (internal-only ingress, port 8081). Both `minReplicas: 0`, `maxReplicas: 3`. `developer-app` ingress idle timeout raised to 1 800 s (30 min ceiling — the maximum ACA allows). |

### 7.3 Identity & secrets

System-assigned managed identity per ACA app. Role assignments authored in Bicep:

| Identity | Role | Scope |
|---|---|---|
| `developer-app` MI | Cosmos DB Built-in Data Contributor | `agent-threads` container |
| `developer-app` MI | Key Vault Secrets User | `github-pat` |
| `developer-app` MI | Cognitive Services User (or Foundry-specific model-deployment role) | Foundry project / model deployment |
| `developer-app` MI | AcrPull | ACR |
| `reviewer-app` MI | Cosmos DB Built-in Data Contributor | `agent-threads` container |
| `reviewer-app` MI | Key Vault Secrets User | `github-pat` |
| `reviewer-app` MI | Cognitive Services User | Foundry project / model deployment |
| `reviewer-app` MI | AcrPull | ACR |

`DefaultAzureCredential` is used everywhere — no static API keys in env vars or code. GitHub PAT is referenced as an ACA secret via `keyvaultref://<vault>/secrets/github-pat` and surfaced to the container as `GITHUB_PAT`.

### 7.4 Container images

Two multi-stage `Dockerfile`s:

- `src/Foundry.Agents.Developer/Dockerfile` — final stage `mcr.microsoft.com/dotnet/sdk:10.0`. SDK kept at runtime because Developer shells out to `dotnet build` / `dotnet test`. `git` ships in the SDK image. ACA emptyDir-style volume mounted at `/work` (5 GB, wiped on pod restart).
- `src/Foundry.Agents.Reviewer/Dockerfile` — final stage `mcr.microsoft.com/dotnet/aspnet:10.0-chiseled`. Review-only; no shell tools, no SDK. Small image, fast cold start.

Both `EXPOSE 8081` and set `ASPNETCORE_URLS=http://+:8081`.

### 7.5 CI/CD (GitHub Actions)

- `.github/workflows/pr.yml` — on `pull_request`: `dotnet restore && dotnet build && dotnet test` (excluding the `IntegrationTests` project unless `RUN_E2E_TESTS=1`).
- `.github/workflows/deploy.yml` — on push to `main`: same as PR, then `docker build` × 2 → `az acr login` + push → `az containerapp update --image …` × 2.

No staging/production split in v1 (D-13). Federated identity (OIDC) from GitHub Actions to a deploy-only service principal — no long-lived secrets in repo settings.

## 8. Testing

| Project | What it covers |
|---|---|
| `Foundry.Agents.Memory.Tests` | Cosmos thread store: round-trip serialization, ETag optimistic concurrency, TTL refresh on write, `CompactAsync` replaces history correctly. **Testcontainers Cosmos linux emulator** — no SDK mocks. |
| `Foundry.Agents.Developer.Tests` | Persona invariants (loaded markdown non-empty, contains required role lines); effort-table mapping (`Xhigh` → 32 768 thinking budget); `MaxReviewRounds` enforcement; Reviewer-unreachable retry semantics (1 + 2 retries with exponential backoff verified at `HttpMessageHandler` level — real retry policy, mocked transport); `assign_dotnet_task` request validation. |
| `Foundry.Agents.Reviewer.Tests` | Persona invariants; verdict mapping (review comments → `Approved`/`ChangesRequested`/`RejectedBlocking`); **refusal-to-merge test** — Reviewer must never invoke any GitHub MCP merge tool. GitHub MCP client is mocked. |
| `Foundry.Agents.Developer.IntegrationTests` | One E2E happy path against the Aspire AppHost (`DistributedApplicationTestingBuilder`): assigns a task to a throwaway GitHub repo, sees PR opened, sees Reviewer called, sees thread compacted in Cosmos. **Skipped by default** unless `RUN_E2E_TESTS=1` and `INTEGRATION_GITHUB_PAT` are set. Not run on PR CI (slow, costs Anthropic tokens). |
| `Foundry.Agents.TestUtils` | Shared fakes: `IGitWorkspace`, `IChatClient`, deterministic clock, deterministic thread-id (ULID) generator. |

**Coverage rule:** unit tests on libraries (`Memory`, `Contracts`) target 85%+. Agent services target the public MCP surface, retry policy, persona invariants, and the compactor. Internal `ChatClientAgent` plumbing is not unit-tested (testing the framework, not our code).

## 9. Non-goals

- **PR merging** — Reviewer approves but never merges. A human merges. Reviewer persona forbids calling any GitHub merge tool; a unit test enforces this.
- **Long-term / cross-thread semantic memory** — per-thread only.
- **Multi-region / HA** — one Cosmos region, one ACA env. Multi-region is a Bicep edit, not a design change.
- **Human-in-the-loop interruption mid-task** — `assign_dotnet_task` runs to completion or `Error`; no pause/inspect/resume. A user who wants control kills the call and re-invokes with the same `threadId`.
- **Reviewer proposing patches** — comments describe what's wrong; Developer fixes.
- **Webhooks / async completion** — Developer's MCP call is fully synchronous from Claude Code's perspective.
- **Dev/staging ACA environment** — single prod environment for v1.
- **Custom Foundry model deployment work** — assumes `claude-opus-4-7` is deployable on Foundry. If not, fallback is to call Anthropic API directly via `Anthropic.SDK` — a one-line `IChatClient` swap per service.

## 10. Risks

1. **Anthropic-on-Foundry availability** — Foundry's Anthropic offerings change. The `IChatClient` abstraction insulates application code, but the `foundry.bicep` model-deployment resource may need swapping (serverless model vs hub-deployed model) depending on what Foundry exposes when this is built. The fallback above keeps the project shippable regardless.
2. **ACA HTTP ingress idle timeout** — `developer-app` runs synchronous calls that can include 3 review rounds against a real Anthropic model. ACA's max ingress idle timeout is 30 min. A pathologically complex task could exceed it. If real-world tasks regularly exceed 30 min, async/webhook is the next design step (out of scope for v1).
3. **`/work/<threadId>` is ephemeral** — already handled by re-cloning on every turn. Do not let this assumption leak into design later.
4. **Foundry GitHub MCP scope** — Foundry-hosted GitHub MCP servers may not expose all the GitHub MCP tools referenced here (`create_pull_request`, `get_pull_request_diff`, `pull_request_review_write`, etc.). If a required tool is missing, fallback is to call the GitHub REST API directly via a small `IGitHubClient` wrapper. The agent persona stays the same; only the tool registration changes.
5. **Cosmos write contention on the same `threadId`** — a caller mistakenly invoking twice in parallel against one thread will see one of the two get a 409. Design treats concurrent same-thread calls as a caller bug; mitigation is documentation, not locking.
