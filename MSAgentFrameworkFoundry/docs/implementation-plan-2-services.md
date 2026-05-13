# Foundry Agents — Plan 2: Services Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Prerequisite:** Plan 1 complete and tagged `plan-1-libraries-complete`.

**Goal:** Build the two ASP.NET Core services — `Foundry.Agents.Developer` and `Foundry.Agents.Reviewer` — plus the Aspire `AppHost` for local dev, so the end-to-end Developer → Reviewer → Developer happy path runs locally and the Developer MCP tool is callable from Claude Code.

**Architecture:** Each service is an ASP.NET Core minimal API hosting a `ModelContextProtocol.AspNetCore` server at `/mcp`. Both use `ChatClientAgent` from `Microsoft.Agents.AI` against a Foundry-deployed Claude Opus 4.7 reached via `Microsoft.Extensions.AI.AzureAIInference`. The Developer additionally shells out to `git`, `dotnet build`, and `dotnet test`, and calls the Reviewer over MCP/HTTP. Both persist threads via `ICosmosThreadStore` from Plan 1. Local orchestration runs through `Foundry.Agents.AppHost` (Aspire), production through ACA + Bicep (Plan 3).

**Tech Stack:** ASP.NET Core 10, `Microsoft.Agents.AI` 1.0.0-rc1, `Microsoft.Extensions.AI` 10.3.0, `Microsoft.Extensions.AI.AzureAIInference` 10.3.0-preview, `ModelContextProtocol.AspNetCore` 1.1.0, Polly 8, Aspire 9.

---

## File structure created by this plan

```
MSAgentFrameworkFoundry/
├── personas/
│   ├── developer.md                                   # NEW
│   └── reviewer.md                                    # NEW
├── src/
│   ├── Foundry.Agents.Developer/
│   │   ├── Foundry.Agents.Developer.csproj
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   ├── appsettings.Development.json
│   │   ├── Properties/launchSettings.json
│   │   ├── ChatClientFactory.cs
│   │   ├── EffortResolver.cs
│   │   ├── GitWorkspace/IGitWorkspace.cs
│   │   ├── GitWorkspace/ProcessGitWorkspace.cs
│   │   ├── GitHubMcp/IGitHubMcpClient.cs
│   │   ├── GitHubMcp/GitHubMcpClient.cs
│   │   ├── Reviewer/IReviewerMcpClient.cs
│   │   ├── Reviewer/ReviewerMcpClient.cs
│   │   ├── Reviewer/ReviewerRetryPolicy.cs
│   │   ├── Compactor/ThreadCompactor.cs
│   │   ├── Mcp/AssignDotnetTaskTool.cs
│   │   └── Orchestration/AssignTaskOrchestrator.cs
│   ├── Foundry.Agents.Reviewer/
│   │   ├── Foundry.Agents.Reviewer.csproj
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   ├── Properties/launchSettings.json
│   │   ├── ChatClientFactory.cs
│   │   ├── EffortResolver.cs
│   │   ├── GitHubMcp/IReviewerGitHubMcpClient.cs
│   │   ├── GitHubMcp/ReviewerGitHubMcpClient.cs
│   │   ├── Mcp/ReviewPullRequestTool.cs
│   │   └── Verdict/VerdictMapper.cs
│   └── Foundry.Agents.AppHost/
│       ├── Foundry.Agents.AppHost.csproj
│       └── Program.cs
└── tests/
    ├── Foundry.Agents.Developer.Tests/
    │   ├── Foundry.Agents.Developer.Tests.csproj
    │   ├── DeveloperPersonaInvariantsTests.cs
    │   ├── EffortResolverTests.cs
    │   ├── ReviewerRetryPolicyTests.cs
    │   ├── MaxReviewRoundsTests.cs
    │   ├── ThreadCompactorTests.cs
    │   └── AssignTaskRequestValidationTests.cs
    ├── Foundry.Agents.Reviewer.Tests/
    │   ├── Foundry.Agents.Reviewer.Tests.csproj
    │   ├── ReviewerPersonaInvariantsTests.cs
    │   ├── VerdictMapperTests.cs
    │   └── RefusalToMergeTests.cs
    └── Foundry.Agents.Developer.IntegrationTests/
        ├── Foundry.Agents.Developer.IntegrationTests.csproj
        └── HappyPathE2ETests.cs
```

---

## Task 1: Persona markdown files

**Files:**
- Create: `personas/developer.md`
- Create: `personas/reviewer.md`

These are checked into the repo root (per spec §5.2). Each agent project embeds the matching file as `persona.md` in Tasks 2 and 14.

- [ ] **Step 1: Write the Developer persona**

Create `personas/developer.md` (the spec mandates four specific instructions — they must appear verbatim or close enough for the persona-invariants test to pass):

```markdown
# Developer Agent

You are a senior .NET 10 C# developer. You take a programming task plus a GitHub repository and deliver the change as a pull request that the Reviewer agent will then approve.

## Hard rules

1. **Always run `dotnet build` and `dotnet test` before pushing.** If either fails, fix the failure and try again. Never push code with broken builds or failing tests.
2. **Never push to the default branch.** Always create a new branch named `agent/<short-thread-id>` and push to it. Open the PR from that branch into the default branch.
3. **One PR per task.** Reuse the same branch across review rounds. Do not open a second PR for the same thread.
4. **Never merge PRs.** Merging is a human responsibility.

## Workflow

1. Read the task description.
2. Clone the target repository.
3. Implement the change, following the existing code style. Add or update tests.
4. Run `dotnet build` and `dotnet test`. Fix until both pass.
5. Commit on the `agent/<short-thread-id>` branch and push.
6. Open a pull request (do not merge it).
7. Hand off to the Reviewer agent.
8. If the Reviewer requests changes, iterate: amend the same branch, push, re-request review.
9. On approval, summarize the work and return the PR URL to the caller.
```

- [ ] **Step 2: Write the Reviewer persona**

Create `personas/reviewer.md`:

```markdown
# Code Reviewer Agent

You are a senior C# code reviewer. You read a pull request and produce one of three verdicts.

## Hard rules

1. **Approve only via the GitHub MCP `pull_request_review_write` (submit_review with APPROVE)** tool. Never invoke any merge tool. Do not call `merge_pull_request`, `merge_branch`, or any tool with "merge" in its name. Merging is a human responsibility, not yours.
2. **Describe what is wrong; do not propose patches.** Your comments explain the problem. The Developer agent writes the fix.
3. **Verdicts are exactly three:** `Approved`, `ChangesRequested`, `RejectedBlocking`. No fourth verdict, no abstention.
4. **Read-only access to the repository.** You inspect diffs, files at the PR's head SHA, and PR metadata. You do not commit, push, or open branches.

## Workflow

1. Fetch the PR diff and changed files.
2. Read carefully. Look for: correctness, security, test coverage, style consistency with the surrounding code, and unintended side-effects.
3. Choose a verdict:
   - **Approved** — the change is good as-is. Submit the review with APPROVE.
   - **ChangesRequested** — issues found that the Developer should fix. Submit REQUEST_CHANGES with itemized comments.
   - **RejectedBlocking** — the PR is out of scope, malicious, or otherwise should not be iterated on. Submit REQUEST_CHANGES with a single comment explaining the block.
4. Return the verdict, comments, and a short summary to the calling Developer agent.
```

- [ ] **Step 3: Commit**

```bash
git add MSAgentFrameworkFoundry/personas
git commit -m "Add Developer + Reviewer persona markdown files"
```

---

## Task 2: Foundry.Agents.Developer project skeleton

**Files:**
- Create: `src/Foundry.Agents.Developer/Foundry.Agents.Developer.csproj`
- Create: `src/Foundry.Agents.Developer/Program.cs`
- Create: `src/Foundry.Agents.Developer/appsettings.json`
- Create: `src/Foundry.Agents.Developer/appsettings.Development.json`

- [ ] **Step 1: Create web project and wire references**

```bash
dotnet new web -n Foundry.Agents.Developer -o src/Foundry.Agents.Developer -f net10.0
dotnet sln MSAgentFrameworkFoundry.slnx add src/Foundry.Agents.Developer/Foundry.Agents.Developer.csproj
dotnet add src/Foundry.Agents.Developer reference src/Foundry.Agents.Contracts
dotnet add src/Foundry.Agents.Developer reference src/Foundry.Agents.Memory
dotnet add src/Foundry.Agents.Developer reference src/Foundry.Agents.ServiceDefaults
dotnet add src/Foundry.Agents.Developer package Microsoft.Agents.AI
dotnet add src/Foundry.Agents.Developer package Microsoft.Extensions.AI
dotnet add src/Foundry.Agents.Developer package Microsoft.Extensions.AI.AzureAIInference
dotnet add src/Foundry.Agents.Developer package Azure.AI.Inference
dotnet add src/Foundry.Agents.Developer package Azure.Identity
dotnet add src/Foundry.Agents.Developer package ModelContextProtocol.AspNetCore
dotnet add src/Foundry.Agents.Developer package Microsoft.Extensions.Http.Resilience
dotnet add src/Foundry.Agents.Developer package Polly
```

- [ ] **Step 2: Embed the persona**

Edit `src/Foundry.Agents.Developer/Foundry.Agents.Developer.csproj`, add inside `<Project>`:

```xml
<ItemGroup>
  <EmbeddedResource Include="..\..\personas\developer.md" LogicalName="persona.md" />
</ItemGroup>
```

- [ ] **Step 3: Write `Program.cs` skeleton**

Replace the generated content:

```csharp
using Foundry.Agents.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
// MCP server, IChatClient, ICosmosThreadStore, IGitWorkspace, etc. wired in later tasks.

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapGet("/", () => Results.Text("Foundry.Agents.Developer"));
app.Run();
```

- [ ] **Step 4: Minimal `appsettings.json` with Serilog and config defaults**

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
  },
  "FoundryChat": {
    "Endpoint": "",
    "DeploymentName": "claude-opus-4-7"
  },
  "Cosmos": {
    "Endpoint": "",
    "DatabaseName": "agentdb",
    "ContainerName": "agent-threads"
  },
  "Developer": {
    "DefaultEffort": "Xhigh",
    "MaxReviewRounds": 3,
    "WorkspaceRoot": "/work",
    "GitHubMcpEndpoint": "",
    "ReviewerMcpEndpoint": ""
  },
  "GitHub": {
    "DefaultBranch": "main"
  }
}
```

`appsettings.Development.json` overrides endpoints to localhost. Aspire injects them at runtime.

- [ ] **Step 5: Build + run + curl**

```bash
dotnet build src/Foundry.Agents.Developer
dotnet run --project src/Foundry.Agents.Developer --urls http://localhost:8081 &
sleep 3
curl -s http://localhost:8081/
curl -s http://localhost:8081/health/live
kill %1
```

Expected: text "Foundry.Agents.Developer" and a 200 from `/health/live`.

- [ ] **Step 6: Commit**

```bash
git add src/Foundry.Agents.Developer MSAgentFrameworkFoundry.slnx
git commit -m "Add Foundry.Agents.Developer skeleton with embedded persona and ServiceDefaults"
```

---

## Task 3: Developer persona invariants test

**Files:**
- Create: `tests/Foundry.Agents.Developer.Tests/Foundry.Agents.Developer.Tests.csproj`
- Create: `tests/Foundry.Agents.Developer.Tests/DeveloperPersonaInvariantsTests.cs`

- [ ] **Step 1: Test project setup**

```bash
dotnet new xunit -n Foundry.Agents.Developer.Tests -o tests/Foundry.Agents.Developer.Tests -f net10.0
rm tests/Foundry.Agents.Developer.Tests/UnitTest1.cs
dotnet sln MSAgentFrameworkFoundry.slnx add tests/Foundry.Agents.Developer.Tests/Foundry.Agents.Developer.Tests.csproj
dotnet add tests/Foundry.Agents.Developer.Tests reference src/Foundry.Agents.Developer
dotnet add tests/Foundry.Agents.Developer.Tests reference src/Foundry.Agents.TestUtils
dotnet add tests/Foundry.Agents.Developer.Tests package FluentAssertions
dotnet add tests/Foundry.Agents.Developer.Tests package NSubstitute
```

- [ ] **Step 2: Make `Program.cs` accessible to the test project**

At the end of `Program.cs`, add:

```csharp
public partial class Program;
```

- [ ] **Step 3: Write the failing persona-invariants test**

Create `DeveloperPersonaInvariantsTests.cs`:

```csharp
using FluentAssertions;
using Foundry.Agents.Contracts.Personas;
using Xunit;

namespace Foundry.Agents.Developer.Tests;

public sealed class DeveloperPersonaInvariantsTests
{
    [Fact]
    public async Task Persona_is_non_empty()
    {
        var persona = await PersonaLoader.LoadAsync(typeof(Program).Assembly);
        persona.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Persona_instructs_dotnet_build_and_dotnet_test_before_pushing()
    {
        var persona = await PersonaLoader.LoadAsync(typeof(Program).Assembly);
        persona.Should().Contain("dotnet build").And.Contain("dotnet test");
        persona.Should().MatchRegex("(?i)before pushing|before push|prior to push");
    }

    [Fact]
    public async Task Persona_forbids_pushing_to_default_branch()
    {
        var persona = await PersonaLoader.LoadAsync(typeof(Program).Assembly);
        persona.Should().MatchRegex("(?i)never push.*default branch");
    }

    [Fact]
    public async Task Persona_says_one_pr_per_task_and_never_merges()
    {
        var persona = await PersonaLoader.LoadAsync(typeof(Program).Assembly);
        persona.Should().MatchRegex("(?i)one PR per task");
        persona.Should().MatchRegex("(?i)never merge");
    }
}
```

- [ ] **Step 4: Run; expect green (persona was written in Task 1 to satisfy these)**

Run: `dotnet test tests/Foundry.Agents.Developer.Tests --filter DeveloperPersonaInvariantsTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add tests/Foundry.Agents.Developer.Tests src/Foundry.Agents.Developer/Program.cs MSAgentFrameworkFoundry.slnx
git commit -m "Lock Developer persona invariants with regex assertions"
```

---

## Task 4: ChatClientFactory + EffortResolver

**Files:**
- Create: `src/Foundry.Agents.Developer/ChatClientFactory.cs`
- Create: `src/Foundry.Agents.Developer/EffortResolver.cs`
- Create: `tests/Foundry.Agents.Developer.Tests/EffortResolverTests.cs`

- [ ] **Step 1: Write the failing EffortResolver test**

Create `EffortResolverTests.cs`:

```csharp
using FluentAssertions;
using Foundry.Agents.Contracts;
using Xunit;

namespace Foundry.Agents.Developer.Tests;

public sealed class EffortResolverTests
{
    private static EffortResolver MakeResolver(EffortLevel configDefault = EffortLevel.Xhigh) =>
        new(configDefault);

    [Fact]
    public void Resolves_request_override_over_thread_and_config()
    {
        MakeResolver().Resolve(requestEffort: EffortLevel.Low, threadEffort: EffortLevel.High)
            .Should().Be(EffortLevel.Low);
    }

    [Fact]
    public void Resolves_thread_value_when_no_request_override()
    {
        MakeResolver().Resolve(requestEffort: null, threadEffort: EffortLevel.Medium)
            .Should().Be(EffortLevel.Medium);
    }

    [Fact]
    public void Resolves_config_default_when_neither_request_nor_thread_set()
    {
        MakeResolver(EffortLevel.Xhigh).Resolve(requestEffort: null, threadEffort: null)
            .Should().Be(EffortLevel.Xhigh);
    }
}
```

- [ ] **Step 2: Run; expect failure**

Run: `dotnet test tests/Foundry.Agents.Developer.Tests --filter EffortResolverTests`
Expected: FAIL — `EffortResolver` does not exist.

- [ ] **Step 3: Implement `EffortResolver.cs`**

```csharp
using Foundry.Agents.Contracts;

namespace Foundry.Agents.Developer;

public sealed class EffortResolver
{
    private readonly EffortLevel _configDefault;
    public EffortResolver(EffortLevel configDefault) => _configDefault = configDefault;

    public EffortLevel Resolve(EffortLevel? requestEffort, EffortLevel? threadEffort)
        => requestEffort ?? threadEffort ?? _configDefault;
}
```

- [ ] **Step 4: Run; expect green**

Run: `dotnet test tests/Foundry.Agents.Developer.Tests --filter EffortResolverTests`
Expected: PASS.

- [ ] **Step 5: Implement `ChatClientFactory.cs` (no unit test — exercises the package surface, not our logic)**

```csharp
using Azure;
using Azure.AI.Inference;
using Azure.Core;
using Azure.Identity;
using Foundry.Agents.Contracts;
using Microsoft.Extensions.AI;

namespace Foundry.Agents.Developer;

public sealed class FoundryChatOptions
{
    public required string Endpoint { get; init; }
    public required string DeploymentName { get; init; }
}

public sealed class ChatClientFactory
{
    private readonly FoundryChatOptions _options;
    private readonly TokenCredential _credential;

    public ChatClientFactory(FoundryChatOptions options, TokenCredential? credential = null)
    {
        _options = options;
        _credential = credential ?? new DefaultAzureCredential();
    }

    /// <summary>Builds an IChatClient targeting the Foundry-deployed Claude Opus 4.7.</summary>
    public IChatClient Create()
    {
        var inference = new ChatCompletionsClient(
            new Uri(_options.Endpoint),
            _credential);
        return inference.AsIChatClient(_options.DeploymentName);
    }

    /// <summary>Builds the ChatOptions for a given effort level. Sets MaxOutputTokens and an Anthropic-style extended-thinking hint.</summary>
    public static ChatOptions ChatOptionsFor(EffortLevel level)
    {
        var budget = EffortBudget.For(level);
        return new ChatOptions
        {
            MaxOutputTokens = budget.MaxOutputTokens,
            AdditionalProperties = new()
            {
                ["thinking"] = new { type = "enabled", budget_tokens = budget.ThinkingBudgetTokens },
            },
        };
    }
}
```

- [ ] **Step 6: Build + commit**

```bash
dotnet build src/Foundry.Agents.Developer
git add src/Foundry.Agents.Developer/ChatClientFactory.cs src/Foundry.Agents.Developer/EffortResolver.cs tests/Foundry.Agents.Developer.Tests/EffortResolverTests.cs
git commit -m "Add ChatClientFactory (AzureAIInference) and EffortResolver"
```

---

## Task 5: IGitWorkspace abstraction + ProcessGitWorkspace + fake update

**Files:**
- Create: `src/Foundry.Agents.Developer/GitWorkspace/IGitWorkspace.cs`
- Create: `src/Foundry.Agents.Developer/GitWorkspace/ProcessGitWorkspace.cs`
- Modify: `src/Foundry.Agents.TestUtils/FakeGitWorkspace.cs` — implement the new interface

- [ ] **Step 1: Define `IGitWorkspace` and forward-declare DTOs**

```csharp
namespace Foundry.Agents.Developer.GitWorkspace;

public sealed record CloneRequest(string RepoUrl, string DestinationPath, string? Branch);
public sealed record ShellResult(int ExitCode, string StdOut, string StdErr);

public interface IGitWorkspace
{
    Task<ShellResult> CloneAsync(CloneRequest request, CancellationToken ct);
    Task<ShellResult> CheckoutNewBranchAsync(string repoPath, string branch, CancellationToken ct);
    Task<ShellResult> CommitAllAsync(string repoPath, string message, CancellationToken ct);
    Task<ShellResult> PushAsync(string repoPath, string branch, CancellationToken ct);
    Task<ShellResult> DotnetBuildAsync(string repoPath, CancellationToken ct);
    Task<ShellResult> DotnetTestAsync(string repoPath, CancellationToken ct);
}
```

- [ ] **Step 2: Implement `ProcessGitWorkspace`**

```csharp
using System.Diagnostics;

namespace Foundry.Agents.Developer.GitWorkspace;

public sealed class ProcessGitWorkspace : IGitWorkspace
{
    private readonly string _githubPat;
    public ProcessGitWorkspace(string githubPat) => _githubPat = githubPat;

    public Task<ShellResult> CloneAsync(CloneRequest r, CancellationToken ct)
    {
        var url = r.RepoUrl.Replace("https://github.com/", $"https://x-access-token:{_githubPat}@github.com/", StringComparison.Ordinal);
        var args = r.Branch is null
            ? $"clone \"{url}\" \"{r.DestinationPath}\""
            : $"clone -b \"{r.Branch}\" \"{url}\" \"{r.DestinationPath}\"";
        return RunAsync("git", args, workingDir: null, ct);
    }

    public Task<ShellResult> CheckoutNewBranchAsync(string repoPath, string branch, CancellationToken ct)
        => RunAsync("git", $"checkout -b \"{branch}\"", repoPath, ct);

    public Task<ShellResult> CommitAllAsync(string repoPath, string message, CancellationToken ct)
        => RunChainAsync(repoPath, ct,
            ("git", "add ."),
            ("git", $"-c user.email=dev@foundry.agent -c user.name=\"Foundry Developer Agent\" commit -m \"{message.Replace("\"", "\\\"")}\""));

    public Task<ShellResult> PushAsync(string repoPath, string branch, CancellationToken ct)
        => RunAsync("git", $"push -u origin \"{branch}\"", repoPath, ct);

    public Task<ShellResult> DotnetBuildAsync(string repoPath, CancellationToken ct)
        => RunAsync("dotnet", "build --nologo", repoPath, ct);

    public Task<ShellResult> DotnetTestAsync(string repoPath, CancellationToken ct)
        => RunAsync("dotnet", "test --nologo --no-build", repoPath, ct);

    private static async Task<ShellResult> RunAsync(string file, string args, string? workingDir, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDir ?? Environment.CurrentDirectory,
        };
        using var p = Process.Start(psi)!;
        var stdoutTask = p.StandardOutput.ReadToEndAsync(ct).AsTask();
        var stderrTask = p.StandardError.ReadToEndAsync(ct).AsTask();
        await p.WaitForExitAsync(ct);
        return new ShellResult(p.ExitCode, await stdoutTask, await stderrTask);
    }

    private static async Task<ShellResult> RunChainAsync(string workingDir, CancellationToken ct, params (string file, string args)[] steps)
    {
        ShellResult? last = null;
        foreach (var (f, a) in steps)
        {
            last = await RunAsync(f, a, workingDir, ct);
            if (last.ExitCode != 0) return last;
        }
        return last!;
    }
}
```

- [ ] **Step 3: Update `FakeGitWorkspace` in `Foundry.Agents.TestUtils` to implement the real interface**

Edit `src/Foundry.Agents.TestUtils/FakeGitWorkspace.cs`:

```csharp
using Foundry.Agents.Developer.GitWorkspace;

namespace Foundry.Agents.TestUtils;

public sealed class FakeGitWorkspace : IGitWorkspace
{
    public List<string> Commands { get; } = new();
    public Queue<ShellResult> Responses { get; } = new();

    private ShellResult Pop() =>
        Responses.Count > 0 ? Responses.Dequeue() : new ShellResult(0, "", "");

    public Task<ShellResult> CloneAsync(CloneRequest r, CancellationToken ct) { Commands.Add($"clone {r.RepoUrl} {r.Branch ?? "<default>"} -> {r.DestinationPath}"); return Task.FromResult(Pop()); }
    public Task<ShellResult> CheckoutNewBranchAsync(string repoPath, string branch, CancellationToken ct) { Commands.Add($"checkout -b {branch} in {repoPath}"); return Task.FromResult(Pop()); }
    public Task<ShellResult> CommitAllAsync(string repoPath, string message, CancellationToken ct) { Commands.Add($"commit -m {message} in {repoPath}"); return Task.FromResult(Pop()); }
    public Task<ShellResult> PushAsync(string repoPath, string branch, CancellationToken ct) { Commands.Add($"push {branch} in {repoPath}"); return Task.FromResult(Pop()); }
    public Task<ShellResult> DotnetBuildAsync(string repoPath, CancellationToken ct) { Commands.Add($"dotnet build in {repoPath}"); return Task.FromResult(Pop()); }
    public Task<ShellResult> DotnetTestAsync(string repoPath, CancellationToken ct) { Commands.Add($"dotnet test in {repoPath}"); return Task.FromResult(Pop()); }
}
```

And add a project reference in `Foundry.Agents.TestUtils.csproj`:

```bash
dotnet add src/Foundry.Agents.TestUtils reference src/Foundry.Agents.Developer
```

> Note: TestUtils now depends on Developer. Acceptable because TestUtils is consumed only by `*.Tests` projects, never by services.

- [ ] **Step 4: Build everything**

Run: `dotnet build MSAgentFrameworkFoundry.slnx`
Expected: green.

- [ ] **Step 5: Commit**

```bash
git add src/Foundry.Agents.Developer/GitWorkspace src/Foundry.Agents.TestUtils/FakeGitWorkspace.cs src/Foundry.Agents.TestUtils/Foundry.Agents.TestUtils.csproj
git commit -m "Add IGitWorkspace + ProcessGitWorkspace; retarget FakeGitWorkspace to the real interface"
```

---

## Task 6: Developer MCP server skeleton + assign_dotnet_task tool stub

**Files:**
- Create: `src/Foundry.Agents.Developer/Mcp/AssignDotnetTaskTool.cs`
- Modify: `src/Foundry.Agents.Developer/Program.cs` — wire MCP
- Create: `tests/Foundry.Agents.Developer.Tests/AssignTaskRequestValidationTests.cs`

- [ ] **Step 1: Failing test — request validation**

```csharp
using FluentAssertions;
using Foundry.Agents.Contracts.Mcp;
using Foundry.Agents.Developer.Mcp;
using Foundry.Agents.Developer;
using NSubstitute;
using Xunit;

namespace Foundry.Agents.Developer.Tests;

public sealed class AssignTaskRequestValidationTests
{
    private static AssignDotnetTaskTool MakeTool() =>
        new(Substitute.For<Orchestration.IAssignTaskOrchestrator>());

    [Fact]
    public async Task Rejects_empty_repo()
    {
        var tool = MakeTool();
        var act = () => tool.HandleAsync(new AssignTaskRequest("", "do the thing", null, null), default);
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*GithubRepo*");
    }

    [Fact]
    public async Task Rejects_repo_without_owner_slash_name()
    {
        var tool = MakeTool();
        var act = () => tool.HandleAsync(new AssignTaskRequest("not-a-repo", "do the thing", null, null), default);
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*owner/repo*");
    }

    [Fact]
    public async Task Rejects_empty_task_description()
    {
        var tool = MakeTool();
        var act = () => tool.HandleAsync(new AssignTaskRequest("octocat/hello", "", null, null), default);
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*TaskDescription*");
    }
}
```

- [ ] **Step 2: Run; expect failure**

Run: `dotnet test tests/Foundry.Agents.Developer.Tests --filter AssignTaskRequestValidationTests`
Expected: FAIL — types don't exist.

- [ ] **Step 3: Add the orchestrator interface stub**

Create `src/Foundry.Agents.Developer/Orchestration/IAssignTaskOrchestrator.cs`:

```csharp
using Foundry.Agents.Contracts.Mcp;

namespace Foundry.Agents.Developer.Orchestration;

public interface IAssignTaskOrchestrator
{
    Task<AssignTaskResult> HandleAsync(AssignTaskRequest request, CancellationToken ct);
}
```

- [ ] **Step 4: Add `AssignDotnetTaskTool` with validation**

Create `src/Foundry.Agents.Developer/Mcp/AssignDotnetTaskTool.cs`:

```csharp
using Foundry.Agents.Contracts.Mcp;
using Foundry.Agents.Developer.Orchestration;
using ModelContextProtocol.Server;

namespace Foundry.Agents.Developer.Mcp;

[McpServerToolType]
public sealed class AssignDotnetTaskTool
{
    private readonly IAssignTaskOrchestrator _orchestrator;
    public AssignDotnetTaskTool(IAssignTaskOrchestrator orchestrator) => _orchestrator = orchestrator;

    [McpServerTool(Name = "assign_dotnet_task"), System.ComponentModel.Description(
        "Assign a .NET 10 C# coding task to the Developer agent. The agent clones the repo, implements the change, opens a PR, and asks the Reviewer to approve. Returns ThreadId, Status, PrUrl, Summary, UnresolvedComments.")]
    public async Task<AssignTaskResult> HandleAsync(AssignTaskRequest request, CancellationToken ct)
    {
        Validate(request);
        return await _orchestrator.HandleAsync(request, ct);
    }

    private static void Validate(AssignTaskRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.GithubRepo))
            throw new ArgumentException("GithubRepo is required.", nameof(r));
        if (!r.GithubRepo.Contains('/', StringComparison.Ordinal) || r.GithubRepo.Split('/').Length != 2)
            throw new ArgumentException("GithubRepo must be in 'owner/repo' format.", nameof(r));
        if (string.IsNullOrWhiteSpace(r.TaskDescription))
            throw new ArgumentException("TaskDescription is required.", nameof(r));
    }
}
```

- [ ] **Step 5: Wire the MCP server in Program.cs**

Replace the body of `Program.cs`:

```csharp
using Foundry.Agents.Developer.Mcp;
using Foundry.Agents.Developer.Orchestration;
using Foundry.Agents.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddProblemDetails();

// Orchestrator wired in Task 8; for now register a placeholder that throws.
builder.Services.AddSingleton<IAssignTaskOrchestrator, ThrowingOrchestrator>();
builder.Services.AddSingleton<AssignDotnetTaskTool>();

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapMcp("/mcp");
app.MapGet("/", () => Results.Text("Foundry.Agents.Developer"));
app.Run();

public partial class Program;

internal sealed class ThrowingOrchestrator : IAssignTaskOrchestrator
{
    public Task<Foundry.Agents.Contracts.Mcp.AssignTaskResult> HandleAsync(
        Foundry.Agents.Contracts.Mcp.AssignTaskRequest request, CancellationToken ct)
        => throw new NotImplementedException("Wired in Task 8");
}
```

- [ ] **Step 6: Run tests; expect green**

Run: `dotnet test tests/Foundry.Agents.Developer.Tests --filter AssignTaskRequestValidationTests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Foundry.Agents.Developer tests/Foundry.Agents.Developer.Tests/AssignTaskRequestValidationTests.cs
git commit -m "Wire MCP server skeleton with assign_dotnet_task validation"
```

---

## Task 7: GitHubMcpClient — Foundry-hosted GitHub MCP wrapper

**Files:**
- Create: `src/Foundry.Agents.Developer/GitHubMcp/IGitHubMcpClient.cs`
- Create: `src/Foundry.Agents.Developer/GitHubMcp/GitHubMcpClient.cs`

This task wraps the Foundry-hosted GitHub MCP server behind a narrow interface so the orchestrator and tests do not bind directly to the MCP client surface. Per spec risk #4, the wrapper can later be swapped for a GitHub REST `IGitHubClient` without touching the orchestrator.

- [ ] **Step 1: Define the interface**

```csharp
namespace Foundry.Agents.Developer.GitHubMcp;

public sealed record CreatePrRequest(string Repo, string Head, string Base, string Title, string Body);
public sealed record CreatePrResponse(int Number, string HtmlUrl);

public interface IGitHubMcpClient
{
    Task<CreatePrResponse> CreatePullRequestAsync(CreatePrRequest request, CancellationToken ct);
}
```

- [ ] **Step 2: Implement against `ModelContextProtocol` client**

```csharp
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Foundry.Agents.Developer.GitHubMcp;

public sealed class GitHubMcpClient : IGitHubMcpClient, IAsyncDisposable
{
    private readonly IMcpClient _client;

    public GitHubMcpClient(IMcpClient client) => _client = client;

    public static async Task<GitHubMcpClient> CreateAsync(Uri endpoint, string? bearerToken, CancellationToken ct)
    {
        var transport = new SseClientTransport(new SseClientTransportOptions
        {
            Endpoint = endpoint,
            AdditionalHeaders = bearerToken is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string> { ["Authorization"] = $"Bearer {bearerToken}" },
        });
        var client = await McpClientFactory.CreateAsync(transport, cancellationToken: ct);
        return new GitHubMcpClient(client);
    }

    public async Task<CreatePrResponse> CreatePullRequestAsync(CreatePrRequest r, CancellationToken ct)
    {
        var args = new Dictionary<string, object?>
        {
            ["owner"] = r.Repo.Split('/')[0],
            ["repo"]  = r.Repo.Split('/')[1],
            ["head"]  = r.Head,
            ["base"]  = r.Base,
            ["title"] = r.Title,
            ["body"]  = r.Body,
        };
        var result = await _client.CallToolAsync("create_pull_request", args, cancellationToken: ct);
        var payload = result.Content.OfType<TextContentBlock>().First().Text;
        // Foundry GitHub MCP returns a JSON object; parse defensively.
        using var doc = System.Text.Json.JsonDocument.Parse(payload);
        return new CreatePrResponse(
            doc.RootElement.GetProperty("number").GetInt32(),
            doc.RootElement.GetProperty("html_url").GetString()!);
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
```

> If the Foundry GitHub MCP server uses streamable-HTTP rather than SSE, swap `SseClientTransport` for `StreamableHttpClientTransport` in `ModelContextProtocol` 1.1.0. The interface stays the same; only the factory changes.

- [ ] **Step 3: Build + commit**

```bash
dotnet build src/Foundry.Agents.Developer
git add src/Foundry.Agents.Developer/GitHubMcp
git commit -m "Add IGitHubMcpClient + Foundry-hosted GitHub MCP wrapper (create_pull_request)"
```

---

## Task 8: AssignTaskOrchestrator happy path — clone, run agent, build/test gate

**Files:**
- Create: `src/Foundry.Agents.Developer/Orchestration/AssignTaskOrchestrator.cs`
- Create: `tests/Foundry.Agents.Developer.Tests/AssignTaskOrchestratorTests.cs`

- [ ] **Step 1: Write the failing BuildFailed test**

```csharp
using FluentAssertions;
using Foundry.Agents.Contracts;
using Foundry.Agents.Contracts.Mcp;
using Foundry.Agents.Developer.GitHubMcp;
using Foundry.Agents.Developer.GitWorkspace;
using Foundry.Agents.Developer.Orchestration;
using Foundry.Agents.Developer.Reviewer;
using Foundry.Agents.Memory;
using Foundry.Agents.TestUtils;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foundry.Agents.Developer.Tests;

public sealed class AssignTaskOrchestratorTests
{
    [Fact]
    public async Task Returns_BuildFailed_when_dotnet_build_exits_nonzero_and_does_not_push_or_open_pr()
    {
        var git = new FakeGitWorkspace();
        git.Responses.Enqueue(new ShellResult(0, "", ""));                // clone
        git.Responses.Enqueue(new ShellResult(1, "", "build error CS1002")); // dotnet build fails

        var store = Substitute.For<ICosmosThreadStore>();
        store.LoadOrCreateAsync(default!, default!, default!, default)
            .ReturnsForAnyArgs(ci => new AgentThread
            {
                Id = ci.ArgAt<string>(0),
                AgentRole = "developer",
                CreatedUtc = DateTimeOffset.UtcNow,
                Messages = { new ThreadMessage("system", "# Developer Persona") },
                ETag = "etag-1",
            });

        var chat = new FakeChatClient();
        chat.QueueResponse("done");  // model returns; orchestrator still runs build/test

        var gh = Substitute.For<IGitHubMcpClient>();
        var reviewer = Substitute.For<IReviewerMcpClient>();

        var orchestrator = new AssignTaskOrchestrator(
            store, git, gh, reviewer,
            chatClientFactory: new TestChatClientFactory(chat),
            effortResolver: new EffortResolver(EffortLevel.Xhigh),
            options: new OrchestratorOptions { WorkspaceRoot = "/tmp/work", MaxReviewRounds = 3, DefaultBranch = "main" },
            ulids: new DeterministicUlidGenerator(),
            logger: NullLogger<AssignTaskOrchestrator>.Instance);

        var result = await orchestrator.HandleAsync(
            new AssignTaskRequest("octocat/hello", "fix bug", null, null), default);

        result.Status.Should().Be(AssignTaskStatus.BuildFailed);
        result.PrUrl.Should().BeNull();
        await gh.DidNotReceiveWithAnyArgs().CreatePullRequestAsync(default!, default);
    }
}

internal sealed class TestChatClientFactory : Foundry.Agents.Developer.IChatClientFactory
{
    private readonly Microsoft.Extensions.AI.IChatClient _client;
    public TestChatClientFactory(Microsoft.Extensions.AI.IChatClient client) => _client = client;
    public Microsoft.Extensions.AI.IChatClient Create() => _client;
}
```

> Note: this introduces `IChatClientFactory` (one-line extraction) and `OrchestratorOptions`. Both are created as part of Step 3.

- [ ] **Step 2: Run; expect failure (types missing)**

Run: `dotnet test tests/Foundry.Agents.Developer.Tests --filter AssignTaskOrchestratorTests`
Expected: FAIL — compile errors on missing types.

- [ ] **Step 3: Extract `IChatClientFactory`, add `OrchestratorOptions`, implement orchestrator skeleton**

Edit `src/Foundry.Agents.Developer/ChatClientFactory.cs`, add interface above the class:

```csharp
public interface IChatClientFactory { Microsoft.Extensions.AI.IChatClient Create(); }
public sealed class ChatClientFactory : IChatClientFactory { /* existing body */ }
```

Create `src/Foundry.Agents.Developer/Orchestration/OrchestratorOptions.cs`:

```csharp
namespace Foundry.Agents.Developer.Orchestration;

public sealed class OrchestratorOptions
{
    public required string WorkspaceRoot { get; init; }
    public required int MaxReviewRounds { get; init; }
    public required string DefaultBranch { get; init; }
}
```

Create `src/Foundry.Agents.Developer/Orchestration/AssignTaskOrchestrator.cs`:

```csharp
using Foundry.Agents.Contracts;
using Foundry.Agents.Contracts.Mcp;
using Foundry.Agents.Developer.GitHubMcp;
using Foundry.Agents.Developer.GitWorkspace;
using Foundry.Agents.Developer.Reviewer;
using Foundry.Agents.Memory;
using Foundry.Agents.TestUtils;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Foundry.Agents.Developer.Orchestration;

public sealed class AssignTaskOrchestrator : IAssignTaskOrchestrator
{
    private readonly ICosmosThreadStore _store;
    private readonly IGitWorkspace _git;
    private readonly IGitHubMcpClient _github;
    private readonly IReviewerMcpClient _reviewer;
    private readonly IChatClientFactory _chatFactory;
    private readonly EffortResolver _effort;
    private readonly OrchestratorOptions _options;
    private readonly DeterministicUlidGenerator _ulids;
    private readonly ILogger<AssignTaskOrchestrator> _logger;

    public AssignTaskOrchestrator(
        ICosmosThreadStore store,
        IGitWorkspace git,
        IGitHubMcpClient github,
        IReviewerMcpClient reviewer,
        IChatClientFactory chatClientFactory,
        EffortResolver effortResolver,
        OrchestratorOptions options,
        DeterministicUlidGenerator ulids,
        ILogger<AssignTaskOrchestrator> logger)
    {
        _store = store; _git = git; _github = github; _reviewer = reviewer;
        _chatFactory = chatClientFactory; _effort = effortResolver;
        _options = options; _ulids = ulids; _logger = logger;
    }

    public async Task<AssignTaskResult> HandleAsync(AssignTaskRequest request, CancellationToken ct)
    {
        var threadId = request.ThreadId ?? _ulids.Next();
        var persona  = await Contracts.Personas.PersonaLoader.LoadAsync(typeof(AssignTaskOrchestrator).Assembly, ct);
        var thread   = await _store.LoadOrCreateAsync(threadId, AgentRole.Developer, persona, ct);
        thread.GithubRepo = request.GithubRepo;
        var effort = _effort.Resolve(request.Effort, thread.Effort);
        thread.Effort = effort;

        var workDir = Path.Combine(_options.WorkspaceRoot, threadId);
        var cloneUrl = $"https://github.com/{request.GithubRepo}.git";
        var clone = await _git.CloneAsync(new CloneRequest(cloneUrl, workDir, Branch: null), ct);
        if (clone.ExitCode != 0)
            return new AssignTaskResult(threadId, AssignTaskStatus.Error, null, $"clone failed: {clone.StdErr}", []);

        // Run the model (single round in this task; iteration loop added in Task 11).
        thread.Messages.Add(new ThreadMessage("user", request.TaskDescription));
        var chatClient = _chatFactory.Create();
        var chatOptions = ChatClientFactory.ChatOptionsFor(effort);
        var response = await chatClient.GetResponseAsync(
            thread.Messages.Select(m => new ChatMessage(new ChatRole(m.Role), m.Content)),
            chatOptions, ct);
        thread.Messages.Add(new ThreadMessage("assistant", response.Text));

        // Build/test gate.
        var build = await _git.DotnetBuildAsync(workDir, ct);
        if (build.ExitCode != 0)
        {
            await _store.SaveAsync(thread, ct);
            return new AssignTaskResult(threadId, AssignTaskStatus.BuildFailed, null, build.StdErr, []);
        }
        var test = await _git.DotnetTestAsync(workDir, ct);
        if (test.ExitCode != 0)
        {
            await _store.SaveAsync(thread, ct);
            return new AssignTaskResult(threadId, AssignTaskStatus.BuildFailed, null, test.StdErr, []);
        }

        // Push + PR + review loop wired in Tasks 9 and 11.
        await _store.SaveAsync(thread, ct);
        return new AssignTaskResult(threadId, AssignTaskStatus.Error, null, "push/PR/review not yet wired", []);
    }
}
```

> The orchestrator references types created in upcoming tasks (`IReviewerMcpClient` — Task 10). Stub the namespace first to compile:

Create `src/Foundry.Agents.Developer/Reviewer/IReviewerMcpClient.cs`:

```csharp
using Foundry.Agents.Contracts.Mcp;

namespace Foundry.Agents.Developer.Reviewer;

public interface IReviewerMcpClient
{
    Task<ReviewResult> ReviewPullRequestAsync(ReviewRequest request, CancellationToken ct);
}
```

- [ ] **Step 4: Run; expect green**

Run: `dotnet test tests/Foundry.Agents.Developer.Tests --filter AssignTaskOrchestratorTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Foundry.Agents.Developer tests/Foundry.Agents.Developer.Tests
git commit -m "Add AssignTaskOrchestrator with clone+model+build/test gate (BuildFailed path)"
```

---

## Task 9: Push + create_pull_request happy path

**Files:**
- Modify: `src/Foundry.Agents.Developer/Orchestration/AssignTaskOrchestrator.cs`
- Modify: `tests/Foundry.Agents.Developer.Tests/AssignTaskOrchestratorTests.cs`

- [ ] **Step 1: Append a happy-path test**

```csharp
[Fact]
public async Task Pushes_branch_and_creates_PR_when_build_and_test_pass()
{
    var git = new FakeGitWorkspace();
    git.Responses.Enqueue(new ShellResult(0, "", "")); // clone
    git.Responses.Enqueue(new ShellResult(0, "", "")); // build
    git.Responses.Enqueue(new ShellResult(0, "", "")); // test
    git.Responses.Enqueue(new ShellResult(0, "", "")); // checkout -b
    git.Responses.Enqueue(new ShellResult(0, "", "")); // commit
    git.Responses.Enqueue(new ShellResult(0, "", "")); // push

    var store = Substitute.For<ICosmosThreadStore>();
    store.LoadOrCreateAsync(default!, default!, default!, default).ReturnsForAnyArgs(ci => new AgentThread
    {
        Id = ci.ArgAt<string>(0), AgentRole = "developer",
        CreatedUtc = DateTimeOffset.UtcNow, Messages = { new ThreadMessage("system", "# p") }, ETag = "e",
    });

    var chat = new FakeChatClient(); chat.QueueResponse("done");
    var gh = Substitute.For<IGitHubMcpClient>();
    gh.CreatePullRequestAsync(default!, default).ReturnsForAnyArgs(new CreatePrResponse(142, "https://github.com/octocat/hello/pull/142"));
    var reviewer = Substitute.For<IReviewerMcpClient>();
    // Pretend the reviewer is unreachable so we stop after PR creation. Task 11 covers review iteration.
    reviewer.ReviewPullRequestAsync(default!, default).ThrowsAsyncForAnyArgs(new HttpRequestException("reviewer down"));

    var orchestrator = new AssignTaskOrchestrator(
        store, git, gh, reviewer,
        new TestChatClientFactory(chat),
        new EffortResolver(EffortLevel.Xhigh),
        new OrchestratorOptions { WorkspaceRoot = "/tmp/work", MaxReviewRounds = 3, DefaultBranch = "main" },
        new DeterministicUlidGenerator(),
        NullLogger<AssignTaskOrchestrator>.Instance);

    var result = await orchestrator.HandleAsync(new AssignTaskRequest("octocat/hello", "fix bug", null, null), default);

    await gh.Received(1).CreatePullRequestAsync(
        Arg.Is<CreatePrRequest>(r => r.Repo == "octocat/hello" && r.Base == "main"),
        Arg.Any<CancellationToken>());
    result.PrUrl.Should().Be("https://github.com/octocat/hello/pull/142");
}
```

- [ ] **Step 2: Run; expect failure (push/PR not implemented)**

Run: `dotnet test tests/Foundry.Agents.Developer.Tests --filter Pushes_branch_and_creates_PR`
Expected: FAIL.

- [ ] **Step 3: Implement push + create_pull_request in the orchestrator**

Replace the "push/PR not wired" tail of `HandleAsync` with:

```csharp
// Push the branch and open the PR.
var branch = $"agent/{threadId[^8..].ToLowerInvariant()}";
var checkoutR = await _git.CheckoutNewBranchAsync(workDir, branch, ct);
if (checkoutR.ExitCode != 0)
    return new AssignTaskResult(threadId, AssignTaskStatus.Error, null, $"checkout failed: {checkoutR.StdErr}", []);

var commitR = await _git.CommitAllAsync(workDir, request.TaskDescription, ct);
if (commitR.ExitCode != 0)
    return new AssignTaskResult(threadId, AssignTaskStatus.Error, null, $"commit failed: {commitR.StdErr}", []);

var pushR = await _git.PushAsync(workDir, branch, ct);
if (pushR.ExitCode != 0)
    return new AssignTaskResult(threadId, AssignTaskStatus.Error, null, $"push failed (branch '{branch}'): {pushR.StdErr}", []);

var pr = await _github.CreatePullRequestAsync(
    new CreatePrRequest(request.GithubRepo, branch, _options.DefaultBranch,
        Title: request.TaskDescription.Length > 72 ? request.TaskDescription[..72] : request.TaskDescription,
        Body:  $"Automated PR from Foundry Developer Agent.\n\nTask: {request.TaskDescription}\n\nThread: {threadId}"),
    ct);
thread.PrNumber = pr.Number;
await _store.SaveAsync(thread, ct);

// Review loop wired in Task 11. For now: try once, propagate any failure as Error.
try
{
    var review = await _reviewer.ReviewPullRequestAsync(
        new ReviewRequest(request.GithubRepo, pr.Number, ThreadId: null, Effort: null), ct);
    // Placeholder — Task 11 turns this into a real iteration loop.
    return new AssignTaskResult(threadId, review.Verdict == ReviewVerdict.Approved
        ? AssignTaskStatus.Approved : AssignTaskStatus.ReviewFailed,
        pr.HtmlUrl, review.Summary, review.Comments.Select(c => c.Body).ToList());
}
catch (Exception ex)
{
    return new AssignTaskResult(threadId, AssignTaskStatus.Error, pr.HtmlUrl,
        $"review attempt failed: {ex.Message}", []);
}
```

- [ ] **Step 4: Run; expect green**

Run: `dotnet test tests/Foundry.Agents.Developer.Tests --filter Pushes_branch_and_creates_PR`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Foundry.Agents.Developer/Orchestration/AssignTaskOrchestrator.cs tests/Foundry.Agents.Developer.Tests/AssignTaskOrchestratorTests.cs
git commit -m "Push agent branch and create_pull_request via GitHub MCP"
```

---

## Task 10: ReviewerMcpClient + retry policy (1 + 2 exponential, cap 30 s)

**Files:**
- Create: `src/Foundry.Agents.Developer/Reviewer/ReviewerMcpClient.cs`
- Create: `src/Foundry.Agents.Developer/Reviewer/ReviewerRetryPolicy.cs`
- Create: `tests/Foundry.Agents.Developer.Tests/ReviewerRetryPolicyTests.cs`

- [ ] **Step 1: Failing test — retry policy attempts 3 times then surfaces**

```csharp
using FluentAssertions;
using Foundry.Agents.Developer.Reviewer;
using Polly;
using Xunit;

namespace Foundry.Agents.Developer.Tests;

public sealed class ReviewerRetryPolicyTests
{
    [Fact]
    public async Task Retries_twice_after_first_failure_for_a_total_of_three_attempts()
    {
        var pipeline = ReviewerRetryPolicy.Build(
            backoffMultiplier: TimeSpan.FromMilliseconds(1),  // shrink in tests
            maxBackoff: TimeSpan.FromMilliseconds(10));

        var attempts = 0;
        var act = async () => await pipeline.ExecuteAsync(_ =>
        {
            attempts++;
            throw new HttpRequestException("nope");
        });

        await act.Should().ThrowAsync<HttpRequestException>();
        attempts.Should().Be(3);
    }

    [Fact]
    public async Task Succeeds_on_third_attempt_returns_value()
    {
        var pipeline = ReviewerRetryPolicy.Build(
            backoffMultiplier: TimeSpan.FromMilliseconds(1),
            maxBackoff: TimeSpan.FromMilliseconds(10));

        var attempts = 0;
        var result = await pipeline.ExecuteAsync(async _ =>
        {
            attempts++;
            if (attempts < 3) throw new HttpRequestException("transient");
            await Task.Yield();
            return 42;
        });

        result.Should().Be(42);
        attempts.Should().Be(3);
    }
}
```

- [ ] **Step 2: Run; expect failure**

Run: `dotnet test tests/Foundry.Agents.Developer.Tests --filter ReviewerRetryPolicyTests`
Expected: FAIL — `ReviewerRetryPolicy` does not exist.

- [ ] **Step 3: Implement `ReviewerRetryPolicy.cs`**

```csharp
using Polly;
using Polly.Retry;

namespace Foundry.Agents.Developer.Reviewer;

public static class ReviewerRetryPolicy
{
    /// <summary>
    /// 1 initial attempt + 2 retries with exponential backoff (cap <paramref name="maxBackoff"/>).
    /// Treats HttpRequestException and 5xx-shaped failures as transient.
    /// </summary>
    public static ResiliencePipeline Build(
        TimeSpan? backoffMultiplier = null,
        TimeSpan? maxBackoff = null)
        => new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>(),
                MaxRetryAttempts = 2,
                BackoffType = DelayBackoffType.Exponential,
                Delay = backoffMultiplier ?? TimeSpan.FromSeconds(1),
                MaxDelay = maxBackoff ?? TimeSpan.FromSeconds(30),
                UseJitter = true,
            })
            .Build();
}
```

- [ ] **Step 4: Implement `ReviewerMcpClient.cs`**

```csharp
using Foundry.Agents.Contracts.Mcp;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Polly;

namespace Foundry.Agents.Developer.Reviewer;

public sealed class ReviewerMcpClient : IReviewerMcpClient, IAsyncDisposable
{
    private readonly IMcpClient _client;
    private readonly ResiliencePipeline _retry;

    public ReviewerMcpClient(IMcpClient client, ResiliencePipeline retry)
    {
        _client = client;
        _retry = retry;
    }

    public static async Task<ReviewerMcpClient> CreateAsync(Uri endpoint, CancellationToken ct)
    {
        var transport = new SseClientTransport(new SseClientTransportOptions { Endpoint = endpoint });
        var client = await McpClientFactory.CreateAsync(transport, cancellationToken: ct);
        return new ReviewerMcpClient(client, ReviewerRetryPolicy.Build());
    }

    public Task<ReviewResult> ReviewPullRequestAsync(ReviewRequest request, CancellationToken ct) =>
        _retry.ExecuteAsync(async token =>
        {
            var args = new Dictionary<string, object?>
            {
                ["GithubRepo"] = request.GithubRepo,
                ["PrNumber"]   = request.PrNumber,
                ["ThreadId"]   = request.ThreadId,
                ["Effort"]     = request.Effort?.ToString(),
            };
            var result = await _client.CallToolAsync("review_pull_request", args, cancellationToken: token);
            var payload = result.Content.OfType<TextContentBlock>().First().Text;
            return System.Text.Json.JsonSerializer.Deserialize<ReviewResult>(payload)!;
        }, ct).AsTask();

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
```

- [ ] **Step 5: Run; expect green**

Run: `dotnet test tests/Foundry.Agents.Developer.Tests --filter ReviewerRetryPolicyTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Foundry.Agents.Developer/Reviewer tests/Foundry.Agents.Developer.Tests/ReviewerRetryPolicyTests.cs
git commit -m "Add ReviewerMcpClient with 1+2 exponential-backoff retry policy"
```

---

## Task 11: Iteration loop — MaxReviewRounds + increment-then-call ordering

**Files:**
- Modify: `src/Foundry.Agents.Developer/Orchestration/AssignTaskOrchestrator.cs`
- Create: `tests/Foundry.Agents.Developer.Tests/MaxReviewRoundsTests.cs`

- [ ] **Step 1: Failing iteration-cap test**

```csharp
using FluentAssertions;
using Foundry.Agents.Contracts;
using Foundry.Agents.Contracts.Mcp;
using Foundry.Agents.Developer.GitHubMcp;
using Foundry.Agents.Developer.GitWorkspace;
using Foundry.Agents.Developer.Orchestration;
using Foundry.Agents.Developer.Reviewer;
using Foundry.Agents.Memory;
using Foundry.Agents.TestUtils;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foundry.Agents.Developer.Tests;

public sealed class MaxReviewRoundsTests
{
    [Fact]
    public async Task Stops_after_MaxReviewRounds_ChangesRequested_and_returns_ReviewFailed()
    {
        var (git, store, gh) = MakeHappyDeps();
        var chat = new FakeChatClient();
        for (int i = 0; i < 10; i++) chat.QueueResponse($"attempt {i}");

        var reviewer = Substitute.For<IReviewerMcpClient>();
        reviewer.ReviewPullRequestAsync(default!, default)
            .ReturnsForAnyArgs(new ReviewResult("r-thread", ReviewVerdict.ChangesRequested,
                new[] { new ReviewComment(null, null, "fix this") }, "needs work"));

        var orch = new AssignTaskOrchestrator(store, git, gh, reviewer,
            new TestChatClientFactory(chat), new EffortResolver(EffortLevel.Xhigh),
            new OrchestratorOptions { WorkspaceRoot = "/tmp/work", MaxReviewRounds = 3, DefaultBranch = "main" },
            new DeterministicUlidGenerator(), NullLogger<AssignTaskOrchestrator>.Instance);

        var result = await orch.HandleAsync(new AssignTaskRequest("octocat/hello", "fix bug", null, null), default);

        result.Status.Should().Be(AssignTaskStatus.ReviewFailed);
        await reviewer.Received(3).ReviewPullRequestAsync(Arg.Any<ReviewRequest>(), Arg.Any<CancellationToken>());
        result.UnresolvedComments.Should().Contain("fix this");
    }

    [Fact]
    public async Task Returns_ReviewFailed_immediately_on_RejectedBlocking()
    {
        var (git, store, gh) = MakeHappyDeps();
        var chat = new FakeChatClient(); chat.QueueResponse("done");
        var reviewer = Substitute.For<IReviewerMcpClient>();
        reviewer.ReviewPullRequestAsync(default!, default).ReturnsForAnyArgs(
            new ReviewResult("r", ReviewVerdict.RejectedBlocking,
                new[] { new ReviewComment(null, null, "out of scope") }, "rejected"));

        var orch = new AssignTaskOrchestrator(store, git, gh, reviewer,
            new TestChatClientFactory(chat), new EffortResolver(EffortLevel.Xhigh),
            new OrchestratorOptions { WorkspaceRoot = "/tmp/work", MaxReviewRounds = 3, DefaultBranch = "main" },
            new DeterministicUlidGenerator(), NullLogger<AssignTaskOrchestrator>.Instance);

        var result = await orch.HandleAsync(new AssignTaskRequest("octocat/hello", "fix bug", null, null), default);

        result.Status.Should().Be(AssignTaskStatus.ReviewFailed);
        await reviewer.Received(1).ReviewPullRequestAsync(Arg.Any<ReviewRequest>(), Arg.Any<CancellationToken>());
    }

    private static (FakeGitWorkspace, ICosmosThreadStore, IGitHubMcpClient) MakeHappyDeps()
    {
        var git = new FakeGitWorkspace();
        for (int i = 0; i < 30; i++) git.Responses.Enqueue(new ShellResult(0, "", ""));
        var store = Substitute.For<ICosmosThreadStore>();
        store.LoadOrCreateAsync(default!, default!, default!, default).ReturnsForAnyArgs(ci => new AgentThread
        {
            Id = ci.ArgAt<string>(0), AgentRole = "developer",
            CreatedUtc = DateTimeOffset.UtcNow, Messages = { new ThreadMessage("system", "# p") }, ETag = "e",
        });
        var gh = Substitute.For<IGitHubMcpClient>();
        gh.CreatePullRequestAsync(default!, default).ReturnsForAnyArgs(new CreatePrResponse(142, "https://example/142"));
        return (git, store, gh);
    }
}
```

- [ ] **Step 2: Run; expect failure (orchestrator does not yet iterate)**

Run: `dotnet test tests/Foundry.Agents.Developer.Tests --filter MaxReviewRoundsTests`
Expected: FAIL.

- [ ] **Step 3: Replace the placeholder review block in `AssignTaskOrchestrator` with the iteration loop**

Replace the `try { var review = await _reviewer...} catch ...` block from Task 9 with:

```csharp
string? reviewThreadId = thread.LinkedReviewThreadId;
ReviewResult? lastReview = null;
for (int round = 1; round <= _options.MaxReviewRounds; round++)
{
    thread.ReviewRound = round;
    await _store.SaveAsync(thread, ct);  // persist before the call — see spec §6.3 idempotency

    try
    {
        lastReview = await _reviewer.ReviewPullRequestAsync(
            new ReviewRequest(request.GithubRepo, pr.Number, reviewThreadId, effort), ct);
    }
    catch (Exception ex)
    {
        return new AssignTaskResult(threadId, AssignTaskStatus.Error, pr.HtmlUrl,
            $"reviewer unreachable after retries: {ex.Message}", []);
    }
    reviewThreadId = lastReview.ThreadId;
    thread.LinkedReviewThreadId = reviewThreadId;

    if (lastReview.Verdict == ReviewVerdict.Approved)
    {
        // /compact is wired in Task 12.
        await _store.SaveAsync(thread, ct);
        return new AssignTaskResult(threadId, AssignTaskStatus.Approved, pr.HtmlUrl, lastReview.Summary, []);
    }
    if (lastReview.Verdict == ReviewVerdict.RejectedBlocking)
    {
        await _store.SaveAsync(thread, ct);
        return new AssignTaskResult(threadId, AssignTaskStatus.ReviewFailed, pr.HtmlUrl, lastReview.Summary,
            lastReview.Comments.Select(c => c.Body).ToList());
    }

    // ChangesRequested — feed comments back to the model, amend commit, push.
    var feedback = "Please address these review comments:\n" +
        string.Join("\n", lastReview.Comments.Select(c => $"- {c.Body}"));
    thread.Messages.Add(new ThreadMessage("user", feedback));
    var amendResp = await chatClient.GetResponseAsync(
        thread.Messages.Select(m => new ChatMessage(new ChatRole(m.Role), m.Content)), chatOptions, ct);
    thread.Messages.Add(new ThreadMessage("assistant", amendResp.Text));

    var b2 = await _git.DotnetBuildAsync(workDir, ct);
    if (b2.ExitCode != 0)
        return new AssignTaskResult(threadId, AssignTaskStatus.BuildFailed, pr.HtmlUrl, b2.StdErr, []);
    var t2 = await _git.DotnetTestAsync(workDir, ct);
    if (t2.ExitCode != 0)
        return new AssignTaskResult(threadId, AssignTaskStatus.BuildFailed, pr.HtmlUrl, t2.StdErr, []);
    await _git.CommitAllAsync(workDir, $"address review round {round}", ct);
    await _git.PushAsync(workDir, branch, ct);
}

await _store.SaveAsync(thread, ct);
return new AssignTaskResult(threadId, AssignTaskStatus.ReviewFailed, pr.HtmlUrl, lastReview!.Summary,
    lastReview.Comments.Select(c => c.Body).ToList());
```

- [ ] **Step 4: Run; expect green**

Run: `dotnet test tests/Foundry.Agents.Developer.Tests`
Expected: all green.

- [ ] **Step 5: Commit**

```bash
git add src/Foundry.Agents.Developer/Orchestration/AssignTaskOrchestrator.cs tests/Foundry.Agents.Developer.Tests/MaxReviewRoundsTests.cs
git commit -m "Iterate review rounds up to MaxReviewRounds; persist round before each call"
```

---

## Task 12: `/compact` on Approved

**Files:**
- Create: `src/Foundry.Agents.Developer/Compactor/ThreadCompactor.cs`
- Create: `tests/Foundry.Agents.Developer.Tests/ThreadCompactorTests.cs`
- Modify: `src/Foundry.Agents.Developer/Orchestration/AssignTaskOrchestrator.cs` — call compactor on Approved

- [ ] **Step 1: Failing test**

```csharp
using FluentAssertions;
using Foundry.Agents.Contracts;
using Foundry.Agents.Developer;
using Foundry.Agents.Developer.Compactor;
using Foundry.Agents.Memory;
using Foundry.Agents.TestUtils;
using Microsoft.Extensions.AI;
using NSubstitute;
using Xunit;

namespace Foundry.Agents.Developer.Tests;

public sealed class ThreadCompactorTests
{
    [Fact]
    public async Task CompactAsync_calls_model_with_summary_prompt_and_persists_summary()
    {
        var chat = new FakeChatClient();
        chat.QueueResponse("Implemented retry policy. PR https://x/142 approved.");
        var store = Substitute.For<ICosmosThreadStore>();
        var thread = new AgentThread
        {
            Id = "tid", AgentRole = "developer",
            CreatedUtc = DateTimeOffset.UtcNow,
            Messages = { new ThreadMessage("system", "# Persona"), new ThreadMessage("user", "fix bug"), new ThreadMessage("assistant", "did") },
            ETag = "e",
        };

        var compactor = new ThreadCompactor(store, new TestChatClientFactory(chat));
        var summary = await compactor.CompactAsync(thread, EffortLevel.Low, default);

        summary.Should().Contain("retry policy");
        await store.Received(1).CompactAsync("tid", AgentRole.Developer, summary, Arg.Any<CancellationToken>());
        chat.Calls.Should().HaveCount(1);
        chat.Calls[0].Messages.Last().Text.Should().Contain("Summarize this conversation in ≤500 tokens");
    }
}
```

- [ ] **Step 2: Run; expect failure**

Run: `dotnet test tests/Foundry.Agents.Developer.Tests --filter ThreadCompactorTests`
Expected: FAIL.

- [ ] **Step 3: Implement `ThreadCompactor.cs`**

```csharp
using Foundry.Agents.Contracts;
using Foundry.Agents.Memory;
using Microsoft.Extensions.AI;

namespace Foundry.Agents.Developer.Compactor;

public sealed class ThreadCompactor
{
    private const string SummaryPrompt =
        "Summarize this conversation in ≤500 tokens, preserving: the original task, the final PR URL, " +
        "key technical decisions, and any commitments to follow up. Output prose, not bullets.";

    private readonly ICosmosThreadStore _store;
    private readonly IChatClientFactory _chatFactory;

    public ThreadCompactor(ICosmosThreadStore store, IChatClientFactory chatFactory)
    {
        _store = store;
        _chatFactory = chatFactory;
    }

    public async Task<string> CompactAsync(AgentThread thread, EffortLevel effort, CancellationToken ct)
    {
        var client = _chatFactory.Create();
        var msgs = thread.Messages.Select(m => new ChatMessage(new ChatRole(m.Role), m.Content)).ToList();
        msgs.Add(new ChatMessage(ChatRole.User, SummaryPrompt));

        var response = await client.GetResponseAsync(msgs, ChatClientFactory.ChatOptionsFor(effort), ct);
        var summary = response.Text.Trim();

        await _store.CompactAsync(thread.Id, AgentRole.Developer, summary, ct);
        return summary;
    }
}
```

- [ ] **Step 4: Wire compactor into orchestrator on Approved**

In `AssignTaskOrchestrator.cs`, change the Approved branch to:

```csharp
if (lastReview.Verdict == ReviewVerdict.Approved)
{
    var compactor = new Compactor.ThreadCompactor(_store, _chatFactory);
    var summary = await compactor.CompactAsync(thread, effort, ct);
    return new AssignTaskResult(threadId, AssignTaskStatus.Approved, pr.HtmlUrl, summary, []);
}
```

- [ ] **Step 5: Run; expect green**

Run: `dotnet test tests/Foundry.Agents.Developer.Tests`
Expected: all green.

- [ ] **Step 6: Commit**

```bash
git add src/Foundry.Agents.Developer/Compactor src/Foundry.Agents.Developer/Orchestration/AssignTaskOrchestrator.cs tests/Foundry.Agents.Developer.Tests/ThreadCompactorTests.cs
git commit -m "Add ThreadCompactor and invoke on Approved verdict"
```

---

## Task 13: Resume semantics — existing prNumber on thread skips re-running model

**Files:**
- Modify: `src/Foundry.Agents.Developer/Orchestration/AssignTaskOrchestrator.cs`
- Append to: `tests/Foundry.Agents.Developer.Tests/AssignTaskOrchestratorTests.cs`

Per spec §4.6: if `assign_dotnet_task` is called with a `threadId` whose thread already has `PrNumber` set and `TaskDescription` is empty or matches the retry-review sentinel `"retry-review"`, skip steps 4–6 and jump straight to the review loop.

- [ ] **Step 1: Failing resume test**

```csharp
[Fact]
public async Task Resume_with_retry_review_sentinel_skips_clone_build_test_push_and_calls_reviewer_directly()
{
    var git = new FakeGitWorkspace();  // no responses queued — clone/build should not be called
    var store = Substitute.For<ICosmosThreadStore>();
    store.LoadOrCreateAsync(default!, default!, default!, default).ReturnsForAnyArgs(ci => new AgentThread
    {
        Id = ci.ArgAt<string>(0), AgentRole = "developer", CreatedUtc = DateTimeOffset.UtcNow,
        Messages = { new ThreadMessage("system", "# p") }, ETag = "e",
        GithubRepo = "octocat/hello", PrNumber = 142, ReviewRound = 1,
        LinkedReviewThreadId = "rt-1",
    });
    var chat = new FakeChatClient(); // no responses — model should not be called
    var gh = Substitute.For<IGitHubMcpClient>();  // no PR creation expected
    var reviewer = Substitute.For<IReviewerMcpClient>();
    reviewer.ReviewPullRequestAsync(default!, default).ReturnsForAnyArgs(
        new ReviewResult("rt-1", ReviewVerdict.Approved, [], "lgtm"));

    var orch = new AssignTaskOrchestrator(store, git, gh, reviewer,
        new TestChatClientFactory(chat), new EffortResolver(EffortLevel.Xhigh),
        new OrchestratorOptions { WorkspaceRoot = "/tmp/work", MaxReviewRounds = 3, DefaultBranch = "main" },
        new DeterministicUlidGenerator(), NullLogger<AssignTaskOrchestrator>.Instance);

    var result = await orch.HandleAsync(
        new AssignTaskRequest("octocat/hello", "retry-review", "existing-thread", null), default);

    result.Status.Should().Be(AssignTaskStatus.Approved);
    git.Commands.Should().BeEmpty();
    chat.Calls.Should().HaveCount(1);  // only the compactor's summary call
    await gh.DidNotReceiveWithAnyArgs().CreatePullRequestAsync(default!, default);
    await reviewer.Received(1).ReviewPullRequestAsync(
        Arg.Is<ReviewRequest>(r => r.PrNumber == 142 && r.ThreadId == "rt-1"),
        Arg.Any<CancellationToken>());
}
```

- [ ] **Step 2: Run; expect failure**

Run: `dotnet test tests/Foundry.Agents.Developer.Tests --filter Resume_with_retry_review_sentinel`
Expected: FAIL — orchestrator does not detect the resume case.

- [ ] **Step 3: Add the resume guard at the top of `HandleAsync`**

After `thread = await _store.LoadOrCreateAsync(...)` and `thread.GithubRepo = ...`, insert:

```csharp
const string RetryReviewSentinel = "retry-review";

bool isResume = thread.PrNumber.HasValue
    && (string.IsNullOrWhiteSpace(request.TaskDescription)
        || string.Equals(request.TaskDescription.Trim(), RetryReviewSentinel, StringComparison.OrdinalIgnoreCase));

if (isResume)
{
    // Skip clone/build/test/push — pretend we're at the review-loop entry with the existing PR.
    var prHtmlUrl = $"https://github.com/{request.GithubRepo}/pull/{thread.PrNumber}";
    return await RunReviewLoopAsync(
        thread, threadId, effort, chatClient: _chatFactory.Create(), chatOptions: ChatClientFactory.ChatOptionsFor(effort),
        workDir: null, branch: null, prNumber: thread.PrNumber!.Value, prHtmlUrl: prHtmlUrl,
        request: request, ct: ct);
}
```

Extract the existing review-loop block into a private method `RunReviewLoopAsync(...)` taking `workDir`/`branch` as nullables; when null, skip the post-`ChangesRequested` "build + commit + push" cycle (we have no local workspace to amend) and instead return `ReviewFailed` so the human re-clones or re-invokes with a real description.

- [ ] **Step 4: Run; expect green**

Run: `dotnet test tests/Foundry.Agents.Developer.Tests`
Expected: all green.

- [ ] **Step 5: Commit**

```bash
git add src/Foundry.Agents.Developer/Orchestration/AssignTaskOrchestrator.cs tests/Foundry.Agents.Developer.Tests/AssignTaskOrchestratorTests.cs
git commit -m "Add resume semantics: existing PR + retry-review sentinel skips re-running model"
```

---

## Task 14: Foundry.Agents.Reviewer project skeleton + persona invariants

**Files:**
- Create: `src/Foundry.Agents.Reviewer/Foundry.Agents.Reviewer.csproj`
- Create: `src/Foundry.Agents.Reviewer/Program.cs`
- Create: `src/Foundry.Agents.Reviewer/appsettings.json`
- Create: `tests/Foundry.Agents.Reviewer.Tests/Foundry.Agents.Reviewer.Tests.csproj`
- Create: `tests/Foundry.Agents.Reviewer.Tests/ReviewerPersonaInvariantsTests.cs`

- [ ] **Step 1: Project setup**

```bash
dotnet new web -n Foundry.Agents.Reviewer -o src/Foundry.Agents.Reviewer -f net10.0
dotnet sln MSAgentFrameworkFoundry.slnx add src/Foundry.Agents.Reviewer/Foundry.Agents.Reviewer.csproj
dotnet add src/Foundry.Agents.Reviewer reference src/Foundry.Agents.Contracts
dotnet add src/Foundry.Agents.Reviewer reference src/Foundry.Agents.Memory
dotnet add src/Foundry.Agents.Reviewer reference src/Foundry.Agents.ServiceDefaults
dotnet add src/Foundry.Agents.Reviewer package Microsoft.Agents.AI
dotnet add src/Foundry.Agents.Reviewer package Microsoft.Extensions.AI.AzureAIInference
dotnet add src/Foundry.Agents.Reviewer package Azure.AI.Inference
dotnet add src/Foundry.Agents.Reviewer package Azure.Identity
dotnet add src/Foundry.Agents.Reviewer package ModelContextProtocol.AspNetCore

dotnet new xunit -n Foundry.Agents.Reviewer.Tests -o tests/Foundry.Agents.Reviewer.Tests -f net10.0
rm tests/Foundry.Agents.Reviewer.Tests/UnitTest1.cs
dotnet sln MSAgentFrameworkFoundry.slnx add tests/Foundry.Agents.Reviewer.Tests/Foundry.Agents.Reviewer.Tests.csproj
dotnet add tests/Foundry.Agents.Reviewer.Tests reference src/Foundry.Agents.Reviewer
dotnet add tests/Foundry.Agents.Reviewer.Tests package FluentAssertions
dotnet add tests/Foundry.Agents.Reviewer.Tests package NSubstitute
```

Embed the persona in `Foundry.Agents.Reviewer.csproj`:

```xml
<ItemGroup>
  <EmbeddedResource Include="..\..\personas\reviewer.md" LogicalName="persona.md" />
</ItemGroup>
```

- [ ] **Step 2: Persona invariants test**

```csharp
using FluentAssertions;
using Foundry.Agents.Contracts.Personas;
using Xunit;

namespace Foundry.Agents.Reviewer.Tests;

public sealed class ReviewerPersonaInvariantsTests
{
    [Fact]
    public async Task Persona_is_non_empty()
    {
        var p = await PersonaLoader.LoadAsync(typeof(Program).Assembly);
        p.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Persona_says_approve_only_via_pull_request_review_write()
    {
        var p = await PersonaLoader.LoadAsync(typeof(Program).Assembly);
        p.Should().Contain("pull_request_review_write");
    }

    [Fact]
    public async Task Persona_forbids_calling_any_merge_tool()
    {
        var p = await PersonaLoader.LoadAsync(typeof(Program).Assembly);
        p.Should().MatchRegex("(?i)never (invoke|call) any merge");
    }

    [Fact]
    public async Task Persona_forbids_proposing_patches_and_lists_three_verdicts()
    {
        var p = await PersonaLoader.LoadAsync(typeof(Program).Assembly);
        p.Should().MatchRegex("(?i)do not propose patches");
        p.Should().Contain("Approved").And.Contain("ChangesRequested").And.Contain("RejectedBlocking");
    }
}
```

- [ ] **Step 3: Minimal `Program.cs`**

```csharp
using Foundry.Agents.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddProblemDetails();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapGet("/", () => Results.Text("Foundry.Agents.Reviewer"));
app.Run();

public partial class Program;
```

- [ ] **Step 4: appsettings.json**

```json
{
  "Serilog": {
    "MinimumLevel": { "Default": "Information",
      "Override": { "Microsoft.AspNetCore": "Warning", "System.Net.Http": "Warning" } },
    "WriteTo": [
      { "Name": "Console", "Args": { "formatter": "Serilog.Formatting.Json.JsonFormatter, Serilog" } }
    ],
    "Enrich": [ "FromLogContext", "WithMachineName" ],
    "Properties": { "Application": "Foundry.Agents.Reviewer" }
  },
  "FoundryChat": { "Endpoint": "", "DeploymentName": "claude-opus-4-7" },
  "Cosmos": { "Endpoint": "", "DatabaseName": "agentdb", "ContainerName": "agent-threads" },
  "Reviewer": { "DefaultEffort": "High", "GitHubMcpEndpoint": "" }
}
```

- [ ] **Step 5: Run; expect green**

Run: `dotnet test tests/Foundry.Agents.Reviewer.Tests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Foundry.Agents.Reviewer tests/Foundry.Agents.Reviewer.Tests MSAgentFrameworkFoundry.slnx
git commit -m "Add Foundry.Agents.Reviewer skeleton with embedded persona + invariants test"
```

---

## Task 15: ReviewerGitHubMcpClient (read-only GitHub MCP wrapper)

**Files:**
- Create: `src/Foundry.Agents.Reviewer/GitHubMcp/IReviewerGitHubMcpClient.cs`
- Create: `src/Foundry.Agents.Reviewer/GitHubMcp/ReviewerGitHubMcpClient.cs`

Reviewer needs only: `get_pull_request_diff`, `get_pull_request_files`, `create_pending_review`, `add_comment_to_pending_review`, `pull_request_review_write` (submit_review). It does **not** import any merge tool — the absence is part of the safety story tested in Task 17.

- [ ] **Step 1: Define the interface — exactly five operations, no merge**

```csharp
namespace Foundry.Agents.Reviewer.GitHubMcp;

public sealed record PrDiff(string UnifiedDiff, string HeadSha);
public sealed record PrFile(string Path, string Status);
public sealed record PendingReviewComment(string? FilePath, int? Line, string Body);

public interface IReviewerGitHubMcpClient
{
    Task<PrDiff> GetPullRequestDiffAsync(string repo, int prNumber, CancellationToken ct);
    Task<IReadOnlyList<PrFile>> GetPullRequestFilesAsync(string repo, int prNumber, CancellationToken ct);
    Task<string> CreatePendingReviewAsync(string repo, int prNumber, CancellationToken ct);
    Task AddCommentToPendingReviewAsync(string repo, int prNumber, string pendingReviewId, PendingReviewComment comment, CancellationToken ct);
    /// <summary>Submits the pending review with verdict APPROVE or REQUEST_CHANGES. No merge.</summary>
    Task SubmitReviewAsync(string repo, int prNumber, string pendingReviewId, string verdict, string body, CancellationToken ct);
}
```

- [ ] **Step 2: Implement against `ModelContextProtocol.Client`**

(Identical shape to Plan 2 T7's `GitHubMcpClient` — wrap `CallToolAsync`, parse JSON payloads. Body omitted for brevity but mirrors T7 with the 5 read-only/review operations.)

```csharp
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Foundry.Agents.Reviewer.GitHubMcp;

public sealed class ReviewerGitHubMcpClient : IReviewerGitHubMcpClient
{
    private readonly IMcpClient _client;
    public ReviewerGitHubMcpClient(IMcpClient client) => _client = client;

    private async Task<string> CallAsync(string tool, Dictionary<string, object?> args, CancellationToken ct)
    {
        var result = await _client.CallToolAsync(tool, args, cancellationToken: ct);
        return result.Content.OfType<TextContentBlock>().First().Text;
    }
    private static (string owner, string repo) Split(string repo)
        { var parts = repo.Split('/'); return (parts[0], parts[1]); }

    public async Task<PrDiff> GetPullRequestDiffAsync(string repo, int prNumber, CancellationToken ct)
    {
        var (o, r) = Split(repo);
        var json = await CallAsync("get_pull_request_diff", new() { ["owner"]=o, ["repo"]=r, ["pullNumber"]=prNumber }, ct);
        using var d = System.Text.Json.JsonDocument.Parse(json);
        return new PrDiff(d.RootElement.GetProperty("diff").GetString()!, d.RootElement.GetProperty("head_sha").GetString()!);
    }

    public async Task<IReadOnlyList<PrFile>> GetPullRequestFilesAsync(string repo, int prNumber, CancellationToken ct)
    {
        var (o, r) = Split(repo);
        var json = await CallAsync("get_pull_request_files", new() { ["owner"]=o, ["repo"]=r, ["pullNumber"]=prNumber }, ct);
        using var d = System.Text.Json.JsonDocument.Parse(json);
        var files = new List<PrFile>();
        foreach (var f in d.RootElement.EnumerateArray())
            files.Add(new PrFile(f.GetProperty("filename").GetString()!, f.GetProperty("status").GetString()!));
        return files;
    }

    public async Task<string> CreatePendingReviewAsync(string repo, int prNumber, CancellationToken ct)
    {
        var (o, r) = Split(repo);
        var json = await CallAsync("create_pending_pull_request_review",
            new() { ["owner"]=o, ["repo"]=r, ["pullNumber"]=prNumber }, ct);
        using var d = System.Text.Json.JsonDocument.Parse(json);
        return d.RootElement.GetProperty("id").GetString()!;
    }

    public Task AddCommentToPendingReviewAsync(string repo, int prNumber, string pendingReviewId, PendingReviewComment c, CancellationToken ct)
    {
        var (o, r) = Split(repo);
        return CallAsync("add_comment_to_pending_review", new()
        {
            ["owner"]=o, ["repo"]=r, ["pullNumber"]=prNumber, ["pendingReviewId"]=pendingReviewId,
            ["path"]=c.FilePath, ["line"]=c.Line, ["body"]=c.Body,
        }, ct);
    }

    public Task SubmitReviewAsync(string repo, int prNumber, string pendingReviewId, string verdict, string body, CancellationToken ct)
    {
        var (o, r) = Split(repo);
        if (verdict is not ("APPROVE" or "REQUEST_CHANGES"))
            throw new ArgumentException($"Verdict must be APPROVE or REQUEST_CHANGES; got '{verdict}'. Merge is not allowed.", nameof(verdict));
        return CallAsync("submit_pending_pull_request_review", new()
        {
            ["owner"]=o, ["repo"]=r, ["pullNumber"]=prNumber, ["pendingReviewId"]=pendingReviewId,
            ["event"]=verdict, ["body"]=body,
        }, ct);
    }
}
```

- [ ] **Step 3: Build + commit**

```bash
dotnet build src/Foundry.Agents.Reviewer
git add src/Foundry.Agents.Reviewer/GitHubMcp
git commit -m "Add ReviewerGitHubMcpClient: 5 read+review tools, no merge"
```

---

## Task 16: review_pull_request tool + VerdictMapper

**Files:**
- Create: `src/Foundry.Agents.Reviewer/Mcp/ReviewPullRequestTool.cs`
- Create: `src/Foundry.Agents.Reviewer/Verdict/VerdictMapper.cs`
- Create: `tests/Foundry.Agents.Reviewer.Tests/VerdictMapperTests.cs`
- Modify: `src/Foundry.Agents.Reviewer/Program.cs` — register the MCP server tool

- [ ] **Step 1: Failing VerdictMapper test**

```csharp
using FluentAssertions;
using Foundry.Agents.Contracts.Mcp;
using Foundry.Agents.Reviewer.Verdict;
using Xunit;

namespace Foundry.Agents.Reviewer.Tests;

public sealed class VerdictMapperTests
{
    [Theory]
    [InlineData("APPROVE",         ReviewVerdict.Approved)]
    [InlineData("approve",         ReviewVerdict.Approved)]
    [InlineData("REQUEST_CHANGES", ReviewVerdict.ChangesRequested)]
    [InlineData("REJECT",          ReviewVerdict.RejectedBlocking)]
    [InlineData("blocking",        ReviewVerdict.RejectedBlocking)]
    public void Maps_recognized_strings_to_enum(string model, ReviewVerdict expected)
    {
        VerdictMapper.FromModelOutput(model).Should().Be(expected);
    }

    [Fact]
    public void Throws_on_unknown_verdict()
    {
        var act = () => VerdictMapper.FromModelOutput("MAYBE_LATER");
        act.Should().Throw<InvalidOperationException>().WithMessage("*unrecognized verdict*");
    }
}
```

- [ ] **Step 2: Run; expect failure**

Run: `dotnet test tests/Foundry.Agents.Reviewer.Tests --filter VerdictMapperTests`
Expected: FAIL.

- [ ] **Step 3: Implement `VerdictMapper.cs`**

```csharp
using Foundry.Agents.Contracts.Mcp;

namespace Foundry.Agents.Reviewer.Verdict;

public static class VerdictMapper
{
    public static ReviewVerdict FromModelOutput(string raw)
    {
        if (raw is null) throw new ArgumentNullException(nameof(raw));
        var v = raw.Trim().ToUpperInvariant();
        return v switch
        {
            "APPROVE" or "APPROVED"                          => ReviewVerdict.Approved,
            "REQUEST_CHANGES" or "CHANGES_REQUESTED"         => ReviewVerdict.ChangesRequested,
            "REJECT" or "REJECTED" or "REJECTED_BLOCKING" or "BLOCKING" => ReviewVerdict.RejectedBlocking,
            _ => throw new InvalidOperationException($"unrecognized verdict from model: '{raw}'"),
        };
    }
}
```

- [ ] **Step 4: Implement `ReviewPullRequestTool`**

```csharp
using Foundry.Agents.Contracts.Mcp;
using Foundry.Agents.Memory;
using Foundry.Agents.Reviewer.GitHubMcp;
using ModelContextProtocol.Server;

namespace Foundry.Agents.Reviewer.Mcp;

[McpServerToolType]
public sealed class ReviewPullRequestTool
{
    private readonly ICosmosThreadStore _store;
    private readonly IReviewerGitHubMcpClient _github;
    private readonly IReviewerOrchestrator _orchestrator;

    public ReviewPullRequestTool(ICosmosThreadStore store, IReviewerGitHubMcpClient github, IReviewerOrchestrator orchestrator)
    {
        _store = store; _github = github; _orchestrator = orchestrator;
    }

    [McpServerTool(Name = "review_pull_request"), System.ComponentModel.Description(
        "Review a GitHub pull request. Returns ThreadId (re-usable across rounds), Verdict (Approved | ChangesRequested | RejectedBlocking), itemized Comments, and a Summary. Never merges.")]
    public Task<ReviewResult> HandleAsync(ReviewRequest request, CancellationToken ct) =>
        _orchestrator.HandleAsync(request, ct);
}

public interface IReviewerOrchestrator
{
    Task<ReviewResult> HandleAsync(ReviewRequest request, CancellationToken ct);
}
```

- [ ] **Step 5: Implement `ReviewerOrchestrator`**

Create `src/Foundry.Agents.Reviewer/Orchestration/ReviewerOrchestrator.cs`:

```csharp
using Foundry.Agents.Contracts;
using Foundry.Agents.Contracts.Mcp;
using Foundry.Agents.Contracts.Personas;
using Foundry.Agents.Memory;
using Foundry.Agents.Reviewer.GitHubMcp;
using Foundry.Agents.Reviewer.Mcp;
using Foundry.Agents.Reviewer.Verdict;
using Foundry.Agents.TestUtils;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Foundry.Agents.Reviewer.Orchestration;

public sealed class ReviewerOrchestrator : IReviewerOrchestrator
{
    private readonly ICosmosThreadStore _store;
    private readonly IReviewerGitHubMcpClient _github;
    private readonly IChatClientFactory _chatFactory;
    private readonly EffortResolver _effort;
    private readonly DeterministicUlidGenerator _ulids;
    private readonly ILogger<ReviewerOrchestrator> _logger;

    public ReviewerOrchestrator(
        ICosmosThreadStore store, IReviewerGitHubMcpClient github,
        IChatClientFactory chatFactory, EffortResolver effort,
        DeterministicUlidGenerator ulids, ILogger<ReviewerOrchestrator> logger)
    {
        _store = store; _github = github; _chatFactory = chatFactory;
        _effort = effort; _ulids = ulids; _logger = logger;
    }

    public async Task<ReviewResult> HandleAsync(ReviewRequest request, CancellationToken ct)
    {
        var threadId = request.ThreadId ?? _ulids.Next();
        var persona  = await PersonaLoader.LoadAsync(typeof(ReviewerOrchestrator).Assembly, ct);
        var thread   = await _store.LoadOrCreateAsync(threadId, AgentRole.Reviewer, persona, ct);
        var effort   = _effort.Resolve(request.Effort, thread.Effort);
        thread.Effort = effort;
        thread.GithubRepo = request.GithubRepo;
        thread.PrNumber = request.PrNumber;

        var diff = await _github.GetPullRequestDiffAsync(request.GithubRepo, request.PrNumber, ct);
        var files = await _github.GetPullRequestFilesAsync(request.GithubRepo, request.PrNumber, ct);

        var prompt =
            $"Review PR #{request.PrNumber} on {request.GithubRepo}. " +
            $"Files changed: {string.Join(", ", files.Select(f => f.Path))}.\n\n" +
            $"Unified diff:\n{diff.UnifiedDiff}\n\n" +
            "Output JSON: { \"verdict\": \"APPROVE|REQUEST_CHANGES|REJECT\", \"summary\": \"...\", \"comments\": [{\"path\":\"...\",\"line\":N,\"body\":\"...\"}] }";
        thread.Messages.Add(new ThreadMessage("user", prompt));

        var client = _chatFactory.Create();
        var resp = await client.GetResponseAsync(
            thread.Messages.Select(m => new ChatMessage(new ChatRole(m.Role), m.Content)),
            ChatClientFactory.ChatOptionsFor(effort), ct);
        thread.Messages.Add(new ThreadMessage("assistant", resp.Text));

        var json = System.Text.Json.JsonDocument.Parse(ExtractJson(resp.Text));
        var verdict = VerdictMapper.FromModelOutput(json.RootElement.GetProperty("verdict").GetString()!);
        var summary = json.RootElement.GetProperty("summary").GetString()!;
        var comments = new List<ReviewComment>();
        if (json.RootElement.TryGetProperty("comments", out var arr))
            foreach (var c in arr.EnumerateArray())
                comments.Add(new ReviewComment(
                    c.TryGetProperty("path", out var p) ? p.GetString() : null,
                    c.TryGetProperty("line", out var l) ? l.GetInt32() : null,
                    c.GetProperty("body").GetString()!));

        // Submit the GitHub review (APPROVE / REQUEST_CHANGES). RejectedBlocking still submits REQUEST_CHANGES + the single block comment.
        var pending = await _github.CreatePendingReviewAsync(request.GithubRepo, request.PrNumber, ct);
        foreach (var c in comments)
            await _github.AddCommentToPendingReviewAsync(request.GithubRepo, request.PrNumber, pending,
                new PendingReviewComment(c.FilePath, c.Line, c.Body), ct);
        var githubVerdict = verdict == ReviewVerdict.Approved ? "APPROVE" : "REQUEST_CHANGES";
        await _github.SubmitReviewAsync(request.GithubRepo, request.PrNumber, pending, githubVerdict, summary, ct);

        await _store.SaveAsync(thread, ct);
        return new ReviewResult(threadId, verdict, comments, summary);
    }

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end < 0) throw new InvalidOperationException($"Model did not return JSON: {text}");
        return text[start..(end + 1)];
    }
}
```

- [ ] **Step 6: Wire DI in `Program.cs`**

```csharp
using Azure.Identity;
using Foundry.Agents.Memory;
using Foundry.Agents.Reviewer;
using Foundry.Agents.Reviewer.GitHubMcp;
using Foundry.Agents.Reviewer.Mcp;
using Foundry.Agents.Reviewer.Orchestration;
using Foundry.Agents.ServiceDefaults;
using Microsoft.Azure.Cosmos;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddProblemDetails();

builder.Services.AddSingleton(sp =>
{
    var endpoint = builder.Configuration["Cosmos:Endpoint"] ?? throw new InvalidOperationException("Cosmos:Endpoint missing");
    return new CosmosClient(endpoint, new DefaultAzureCredential());
});
builder.Services.AddSingleton<ICosmosThreadStore>(sp =>
{
    var c = sp.GetRequiredService<CosmosClient>();
    var container = c.GetContainer(builder.Configuration["Cosmos:DatabaseName"]!, builder.Configuration["Cosmos:ContainerName"]!);
    return new CosmosThreadStore(container, TimeProvider.System);
});
builder.Services.AddSingleton(new FoundryChatOptions
{
    Endpoint = builder.Configuration["FoundryChat:Endpoint"]!,
    DeploymentName = builder.Configuration["FoundryChat:DeploymentName"]!,
});
builder.Services.AddSingleton<IChatClientFactory, ChatClientFactory>();
builder.Services.AddSingleton(new EffortResolver(
    Enum.Parse<Foundry.Agents.Contracts.EffortLevel>(builder.Configuration["Reviewer:DefaultEffort"] ?? "High")));
builder.Services.AddSingleton<Foundry.Agents.TestUtils.DeterministicUlidGenerator>();
builder.Services.AddSingleton<IReviewerGitHubMcpClient>(_ => throw new NotImplementedException("Wire IMcpClient at runtime"));
builder.Services.AddSingleton<IReviewerOrchestrator, ReviewerOrchestrator>();
builder.Services.AddSingleton<ReviewPullRequestTool>();

builder.Services.AddMcpServer().WithHttpTransport().WithToolsFromAssembly();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapMcp("/mcp");
app.MapGet("/", () => Results.Text("Foundry.Agents.Reviewer"));
app.Run();

public partial class Program;
```

> `EffortResolver` and `IChatClientFactory` are declared in `Foundry.Agents.Developer`. To avoid Reviewer referencing Developer, **move both into a shared helper inside `Foundry.Agents.Contracts`** (rename namespace to `Foundry.Agents.Contracts.Chat`). One small refactor commit before this task. Add it to the checklist:

- Move `EffortResolver` and `IChatClientFactory` / `ChatClientFactory` / `FoundryChatOptions` into `Foundry.Agents.Contracts/Chat/` and update references in `Foundry.Agents.Developer`.

- [ ] **Step 7: Run; expect green VerdictMapperTests**

Run: `dotnet test tests/Foundry.Agents.Reviewer.Tests`
Expected: PASS (persona + verdict tests).

- [ ] **Step 8: Commit**

```bash
git add .
git commit -m "Add ReviewPullRequestTool, ReviewerOrchestrator, VerdictMapper; move chat helpers into Contracts.Chat"
```

---

## Task 17: Refusal-to-merge invariant test

**Files:**
- Create: `tests/Foundry.Agents.Reviewer.Tests/RefusalToMergeTests.cs`

- [ ] **Step 1: Test asserts interface has no merge method and that SubmitReview rejects MERGE**

```csharp
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Foundry.Agents.Reviewer.GitHubMcp;
using NSubstitute;
using Xunit;

namespace Foundry.Agents.Reviewer.Tests;

public sealed class RefusalToMergeTests
{
    [Fact]
    public void IReviewerGitHubMcpClient_exposes_no_method_with_merge_in_its_name()
    {
        var members = typeof(IReviewerGitHubMcpClient).GetMethods();
        members.Should().OnlyContain(m => !m.Name.Contains("Merge", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SubmitReview_rejects_any_verdict_that_is_not_APPROVE_or_REQUEST_CHANGES()
    {
        var client = new ReviewerGitHubMcpClient(Substitute.For<ModelContextProtocol.Client.IMcpClient>());
        var act = () => client.SubmitReviewAsync("o/r", 1, "pid", "MERGE", "ship it", default);
        act.Should().ThrowAsync<ArgumentException>().WithMessage("*Merge is not allowed*");
    }
}
```

- [ ] **Step 2: Run; expect green (Task 15 already enforces these)**

Run: `dotnet test tests/Foundry.Agents.Reviewer.Tests --filter RefusalToMergeTests`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add tests/Foundry.Agents.Reviewer.Tests/RefusalToMergeTests.cs
git commit -m "Pin refusal-to-merge as an invariant: no Merge in interface, no MERGE in SubmitReview"
```

---

## Task 18: Foundry.Agents.AppHost (Aspire, dev-only)

**Files:**
- Create: `src/Foundry.Agents.AppHost/Foundry.Agents.AppHost.csproj`
- Create: `src/Foundry.Agents.AppHost/Program.cs`

- [ ] **Step 1: Create AppHost**

```bash
dotnet new aspire-apphost -n Foundry.Agents.AppHost -o src/Foundry.Agents.AppHost -f net10.0
dotnet sln MSAgentFrameworkFoundry.slnx add src/Foundry.Agents.AppHost/Foundry.Agents.AppHost.csproj
dotnet add src/Foundry.Agents.AppHost reference src/Foundry.Agents.Developer
dotnet add src/Foundry.Agents.AppHost reference src/Foundry.Agents.Reviewer
dotnet add src/Foundry.Agents.AppHost package Aspire.Hosting.Azure.CosmosDB
```

- [ ] **Step 2: Mark AppHost as non-packable**

Edit the csproj, add inside the existing `<PropertyGroup>`:

```xml
<IsPackable>false</IsPackable>
```

- [ ] **Step 3: Write `Program.cs`**

```csharp
using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var cosmos = builder.AddAzureCosmosDB("threads").RunAsEmulator();
var threads = cosmos.AddCosmosDatabase("agentdb").AddContainer("agent-threads", "/agentRole");

var foundryEndpoint = builder.AddParameter("anthropic-foundry-endpoint", secret: false);
var githubMcp = builder.AddParameter("foundry-github-mcp-endpoint", secret: false);
var githubPat = builder.AddParameter("github-pat", secret: true);

var reviewer = builder.AddProject<Projects.Foundry_Agents_Reviewer>("reviewer")
    .WithReference(threads)
    .WithEnvironment("FoundryChat__Endpoint", foundryEndpoint)
    .WithEnvironment("Reviewer__GitHubMcpEndpoint", githubMcp);

builder.AddProject<Projects.Foundry_Agents_Developer>("developer")
    .WithReference(threads)
    .WithEnvironment("FoundryChat__Endpoint", foundryEndpoint)
    .WithEnvironment("Developer__GitHubMcpEndpoint", githubMcp)
    .WithEnvironment("GITHUB_PAT", githubPat)
    .WithReference(reviewer)
    .WithEnvironment("Developer__ReviewerMcpEndpoint", reviewer.GetEndpoint("https"));

builder.Build().Run();
```

- [ ] **Step 4: User-secrets seed**

```bash
dotnet user-secrets set "Parameters:anthropic-foundry-endpoint" "https://<your-foundry>.services.ai.azure.com" --project src/Foundry.Agents.AppHost
dotnet user-secrets set "Parameters:foundry-github-mcp-endpoint" "https://<your-foundry-github-mcp-url>/mcp" --project src/Foundry.Agents.AppHost
dotnet user-secrets set "Parameters:github-pat" "<your PAT>" --project src/Foundry.Agents.AppHost
```

- [ ] **Step 5: Run AppHost, smoke-test the Developer MCP**

```bash
dotnet run --project src/Foundry.Agents.AppHost
```

Aspire prints URLs. Find the Developer's public URL, then:

```bash
claude mcp add foundry-dev https://localhost:<port>/mcp
# In a separate shell, exercise it:
claude --print "use foundry-dev assign_dotnet_task on github.com/<your-fork> with task 'add a Hello() method'"
```

Expected: Dashboard shows both services healthy; `assign_dotnet_task` runs to completion (Approved or ReviewFailed).

- [ ] **Step 6: Commit**

```bash
git add src/Foundry.Agents.AppHost MSAgentFrameworkFoundry.slnx
git commit -m "Add Foundry.Agents.AppHost (Aspire dev-only, IsPackable=false)"
```

---

## Task 19: Foundry.Agents.Developer.IntegrationTests (gated E2E)

**Files:**
- Create: `tests/Foundry.Agents.Developer.IntegrationTests/Foundry.Agents.Developer.IntegrationTests.csproj`
- Create: `tests/Foundry.Agents.Developer.IntegrationTests/HappyPathE2ETests.cs`

- [ ] **Step 1: Spin up the test project**

```bash
dotnet new xunit -n Foundry.Agents.Developer.IntegrationTests -o tests/Foundry.Agents.Developer.IntegrationTests -f net10.0
rm tests/Foundry.Agents.Developer.IntegrationTests/UnitTest1.cs
dotnet sln MSAgentFrameworkFoundry.slnx add tests/Foundry.Agents.Developer.IntegrationTests/Foundry.Agents.Developer.IntegrationTests.csproj
dotnet add tests/Foundry.Agents.Developer.IntegrationTests reference src/Foundry.Agents.AppHost
dotnet add tests/Foundry.Agents.Developer.IntegrationTests reference src/Foundry.Agents.Contracts
dotnet add tests/Foundry.Agents.Developer.IntegrationTests package Aspire.Hosting.Testing
dotnet add tests/Foundry.Agents.Developer.IntegrationTests package FluentAssertions
dotnet add tests/Foundry.Agents.Developer.IntegrationTests package ModelContextProtocol
```

- [ ] **Step 2: Write the gated happy-path test**

```csharp
using Aspire.Hosting;
using FluentAssertions;
using Foundry.Agents.Contracts.Mcp;
using ModelContextProtocol.Client;
using Xunit;

namespace Foundry.Agents.Developer.IntegrationTests;

public sealed class HappyPathE2ETests
{
    private static readonly bool Enabled =
        Environment.GetEnvironmentVariable("RUN_E2E_TESTS") == "1" &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("INTEGRATION_GITHUB_PAT")) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("INTEGRATION_TARGET_REPO"));

    [SkippableFact]
    public async Task assign_dotnet_task_opens_pr_and_returns_approved_status()
    {
        Skip.IfNot(Enabled, "Set RUN_E2E_TESTS=1, INTEGRATION_GITHUB_PAT, INTEGRATION_TARGET_REPO to run.");

        await using var app = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Foundry_Agents_AppHost>()
            .BuildAsync();
        await app.StartAsync();
        var developerUrl = app.GetEndpoint("developer", "https");

        await using var transport = new SseClientTransport(new SseClientTransportOptions { Endpoint = new Uri(new Uri(developerUrl), "/mcp") });
        await using var client = await McpClientFactory.CreateAsync(transport);

        var args = new Dictionary<string, object?>
        {
            ["GithubRepo"] = Environment.GetEnvironmentVariable("INTEGRATION_TARGET_REPO"),
            ["TaskDescription"] = "Add a Hello() method to the public API that returns the string \"hi\".",
            ["Effort"] = "Low",
        };
        var result = await client.CallToolAsync("assign_dotnet_task", args);
        var payload = result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().First().Text;
        var dto = System.Text.Json.JsonSerializer.Deserialize<AssignTaskResult>(payload)!;

        dto.Status.Should().BeOneOf(AssignTaskStatus.Approved, AssignTaskStatus.ReviewFailed);
        dto.PrUrl.Should().NotBeNullOrWhiteSpace();
    }
}
```

Add `Xunit.SkippableFact` package:

```bash
dotnet add tests/Foundry.Agents.Developer.IntegrationTests package Xunit.SkippableFact
```

- [ ] **Step 3: Verify the skipped-default behavior**

Run: `dotnet test tests/Foundry.Agents.Developer.IntegrationTests`
Expected: 1 test skipped (because `RUN_E2E_TESTS` is unset). Not run on PR CI.

- [ ] **Step 4: Commit**

```bash
git add tests/Foundry.Agents.Developer.IntegrationTests MSAgentFrameworkFoundry.slnx
git commit -m "Add Foundry.Agents.Developer.IntegrationTests gated on RUN_E2E_TESTS=1"
```

---

## Final verification

- [ ] **Step 1: Build**

Run: `dotnet build MSAgentFrameworkFoundry.slnx`
Expected: All 11 projects build; zero warnings (treated as errors).

- [ ] **Step 2: Run all unit tests**

Run: `dotnet test MSAgentFrameworkFoundry.slnx --filter "FullyQualifiedName!~IntegrationTests"`
Expected: all green across Contracts, Memory, Developer, Reviewer tests.

- [ ] **Step 3: Local end-to-end smoke**

Start the AppHost (Task 18 Step 5), call `assign_dotnet_task` against a throwaway test repo via Claude Code, observe the PR opened, the Reviewer called, and either `Approved` (with `/compact` applied) or `ReviewFailed`.

- [ ] **Step 4: Tag**

```bash
git tag plan-2-services-complete
```

Plan 2 is done when:
- All 11 projects build.
- Persona invariants, verdict mapping, refusal-to-merge, retry policy, MaxReviewRounds, resume-semantics, and ThreadCompactor tests are all green.
- AppHost starts both services locally and the Developer MCP responds at `/mcp`.

Proceed to [Plan 3: Infrastructure & CI/CD](implementation-plan-3-infra.md).
