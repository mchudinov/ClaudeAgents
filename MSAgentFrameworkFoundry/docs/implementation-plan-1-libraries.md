# Foundry Agents — Plan 1: Libraries Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the four shared libraries (`Foundry.Agents.Contracts`, `Foundry.Agents.Memory`, `Foundry.Agents.ServiceDefaults`, `Foundry.Agents.TestUtils`) and their unit tests, so the Cosmos thread store and persona/effort/DTO contracts are independently testable before any agent service depends on them.

**Architecture:** Greenfield .NET 10 C# solution with Central Package Management. Four libraries are added to the empty `.slnx` one at a time, each with its own xUnit test project. The Cosmos thread store is tested against the real Cosmos linux emulator via Testcontainers (no SDK mocks). All libraries follow the spec's locked decisions (D-3, D-7, D-8, D-9, D-14, D-15).

**Tech Stack:** .NET 10, C#, xUnit, FluentAssertions, NSubstitute, Microsoft.Azure.Cosmos 3.43.x, Testcontainers.CosmosDb, Serilog 8.x, OpenTelemetry 1.10.x, Microsoft.Extensions.* 10.3.0.

---

## File structure created by this plan

```
MSAgentFrameworkFoundry/
├── MSAgentFrameworkFoundry.slnx       # updated: adds 8 projects
├── global.json                        # NEW: pins .NET 10 SDK
├── Directory.Build.props              # NEW: shared csproj defaults
├── Directory.Packages.props           # NEW: central package management + pins
├── src/
│   ├── Foundry.Agents.Contracts/
│   │   ├── Foundry.Agents.Contracts.csproj
│   │   ├── EffortLevel.cs
│   │   ├── EffortBudget.cs
│   │   ├── Personas/PersonaLoader.cs
│   │   ├── Mcp/AssignTaskRequest.cs
│   │   ├── Mcp/AssignTaskResult.cs
│   │   ├── Mcp/AssignTaskStatus.cs
│   │   ├── Mcp/ReviewRequest.cs
│   │   ├── Mcp/ReviewResult.cs
│   │   ├── Mcp/ReviewVerdict.cs
│   │   └── Mcp/ReviewComment.cs
│   ├── Foundry.Agents.Memory/
│   │   ├── Foundry.Agents.Memory.csproj
│   │   ├── AgentRole.cs
│   │   ├── AgentThread.cs
│   │   ├── ThreadMessage.cs
│   │   ├── ICosmosThreadStore.cs
│   │   ├── CosmosThreadStore.cs
│   │   └── CosmosThreadStoreOptions.cs
│   ├── Foundry.Agents.ServiceDefaults/
│   │   ├── Foundry.Agents.ServiceDefaults.csproj
│   │   └── ServiceDefaultsExtensions.cs
│   └── Foundry.Agents.TestUtils/
│       ├── Foundry.Agents.TestUtils.csproj
│       ├── FixedClock.cs
│       ├── DeterministicUlidGenerator.cs
│       ├── FakeChatClient.cs
│       └── FakeGitWorkspace.cs
└── tests/
    ├── Foundry.Agents.Contracts.Tests/
    │   ├── Foundry.Agents.Contracts.Tests.csproj
    │   ├── Resources/test-persona.md
    │   ├── EffortBudgetTests.cs
    │   └── PersonaLoaderTests.cs
    └── Foundry.Agents.Memory.Tests/
        ├── Foundry.Agents.Memory.Tests.csproj
        ├── CosmosEmulatorFixture.cs
        └── CosmosThreadStoreTests.cs
```

---

## Task 1: Solution seed (global.json, Directory.Build.props, Directory.Packages.props)

**Files:**
- Create: `MSAgentFrameworkFoundry/global.json`
- Create: `MSAgentFrameworkFoundry/Directory.Build.props`
- Create: `MSAgentFrameworkFoundry/Directory.Packages.props`

- [ ] **Step 1: Pin the .NET 10 SDK**

Create `global.json`:

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature",
    "allowPrerelease": false
  }
}
```

- [ ] **Step 2: Shared csproj defaults**

Create `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Central package versions with locked pins**

Create `Directory.Packages.props`:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <!-- Locked pins per spec & user choice -->
    <PackageVersion Include="Microsoft.Agents.AI" Version="1.0.0-rc1" />
    <PackageVersion Include="Microsoft.Extensions.AI" Version="10.3.0" />
    <PackageVersion Include="Microsoft.Extensions.AI.Abstractions" Version="10.3.0" />
    <PackageVersion Include="Microsoft.Extensions.AI.AzureAIInference" Version="10.3.0-preview" />
    <PackageVersion Include="Azure.AI.Inference" Version="1.0.0-beta.5" />
    <PackageVersion Include="ModelContextProtocol" Version="1.1.0" />
    <PackageVersion Include="ModelContextProtocol.AspNetCore" Version="1.1.0" />
    <!-- Azure -->
    <PackageVersion Include="Azure.Identity" Version="1.13.1" />
    <PackageVersion Include="Microsoft.Azure.Cosmos" Version="3.43.1" />
    <!-- Hosting / ASP.NET -->
    <PackageVersion Include="Microsoft.Extensions.Hosting" Version="9.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Http" Version="9.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Http.Resilience" Version="9.0.0" />
    <PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="9.0.0" />
    <!-- Observability -->
    <PackageVersion Include="Serilog.AspNetCore" Version="8.0.3" />
    <PackageVersion Include="Serilog.Settings.Configuration" Version="8.0.4" />
    <PackageVersion Include="Serilog.Sinks.Console" Version="6.0.0" />
    <PackageVersion Include="OpenTelemetry.Extensions.Hosting" Version="1.10.0" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.10.1" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.Http" Version="1.10.0" />
    <PackageVersion Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.10.0" />
    <!-- Test stack -->
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageVersion Include="xunit" Version="2.9.2" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.0.0" />
    <PackageVersion Include="FluentAssertions" Version="7.0.0" />
    <PackageVersion Include="NSubstitute" Version="5.3.0" />
    <PackageVersion Include="Testcontainers.CosmosDb" Version="4.0.0" />
    <!-- Polly (used in Plan 2) -->
    <PackageVersion Include="Polly" Version="8.5.0" />
    <!-- Aspire (used in Plan 2) -->
    <PackageVersion Include="Aspire.Hosting.AppHost" Version="9.0.0" />
    <PackageVersion Include="Aspire.Hosting.Azure.CosmosDB" Version="9.0.0" />
    <PackageVersion Include="Aspire.Hosting.Testing" Version="9.0.0" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Verify the empty solution still builds**

Run: `cd MSAgentFrameworkFoundry && dotnet build MSAgentFrameworkFoundry.slnx`
Expected: `Build succeeded` (zero projects).

- [ ] **Step 5: Commit**

```bash
git add MSAgentFrameworkFoundry/global.json MSAgentFrameworkFoundry/Directory.Build.props MSAgentFrameworkFoundry/Directory.Packages.props
git commit -m "Seed Foundry solution: pin .NET 10 SDK, enable CPM, lock package versions"
```

---

## Task 2: Foundry.Agents.Contracts project + EffortLevel + EffortBudget

**Files:**
- Create: `src/Foundry.Agents.Contracts/Foundry.Agents.Contracts.csproj`
- Create: `src/Foundry.Agents.Contracts/EffortLevel.cs`
- Create: `src/Foundry.Agents.Contracts/EffortBudget.cs`

- [ ] **Step 1: Create the classlib project**

Run from `MSAgentFrameworkFoundry/`:

```bash
dotnet new classlib -n Foundry.Agents.Contracts -o src/Foundry.Agents.Contracts -f net10.0
rm src/Foundry.Agents.Contracts/Class1.cs
dotnet sln MSAgentFrameworkFoundry.slnx add src/Foundry.Agents.Contracts/Foundry.Agents.Contracts.csproj
```

- [ ] **Step 2: Write `EffortLevel.cs`**

```csharp
namespace Foundry.Agents.Contracts;

public enum EffortLevel
{
    Low,
    Medium,
    High,
    Xhigh,
    Max,
}
```

- [ ] **Step 3: Write `EffortBudget.cs` with the spec table**

```csharp
namespace Foundry.Agents.Contracts;

public readonly record struct EffortBudget(int ThinkingBudgetTokens, int MaxOutputTokens)
{
    public static EffortBudget For(EffortLevel level) => level switch
    {
        EffortLevel.Low    => new EffortBudget(1_024,  4_096),
        EffortLevel.Medium => new EffortBudget(4_096,  8_192),
        EffortLevel.High   => new EffortBudget(16_384, 16_384),
        EffortLevel.Xhigh  => new EffortBudget(32_768, 32_768),
        EffortLevel.Max    => new EffortBudget(64_000, 64_000),
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown effort level"),
    };
}
```

- [ ] **Step 4: Build**

Run: `dotnet build src/Foundry.Agents.Contracts/Foundry.Agents.Contracts.csproj`
Expected: `Build succeeded`.

- [ ] **Step 5: Commit**

```bash
git add src/Foundry.Agents.Contracts MSAgentFrameworkFoundry.slnx
git commit -m "Add Foundry.Agents.Contracts: EffortLevel + EffortBudget table"
```

---

## Task 3: MCP request/result DTOs

**Files:**
- Create: `src/Foundry.Agents.Contracts/Mcp/AssignTaskRequest.cs`
- Create: `src/Foundry.Agents.Contracts/Mcp/AssignTaskResult.cs`
- Create: `src/Foundry.Agents.Contracts/Mcp/AssignTaskStatus.cs`
- Create: `src/Foundry.Agents.Contracts/Mcp/ReviewRequest.cs`
- Create: `src/Foundry.Agents.Contracts/Mcp/ReviewResult.cs`
- Create: `src/Foundry.Agents.Contracts/Mcp/ReviewVerdict.cs`
- Create: `src/Foundry.Agents.Contracts/Mcp/ReviewComment.cs`

- [ ] **Step 1: Developer surface — `AssignTaskStatus.cs`**

```csharp
namespace Foundry.Agents.Contracts.Mcp;

public enum AssignTaskStatus
{
    Approved,
    ReviewFailed,
    BuildFailed,
    Error,
}
```

- [ ] **Step 2: `AssignTaskRequest.cs` and `AssignTaskResult.cs`**

```csharp
// AssignTaskRequest.cs
namespace Foundry.Agents.Contracts.Mcp;

public sealed record AssignTaskRequest(
    string GithubRepo,
    string TaskDescription,
    string? ThreadId,
    EffortLevel? Effort);
```

```csharp
// AssignTaskResult.cs
namespace Foundry.Agents.Contracts.Mcp;

public sealed record AssignTaskResult(
    string ThreadId,
    AssignTaskStatus Status,
    string? PrUrl,
    string? Summary,
    IReadOnlyList<string> UnresolvedComments);
```

- [ ] **Step 3: Reviewer surface — `ReviewVerdict.cs` and `ReviewComment.cs`**

```csharp
// ReviewVerdict.cs
namespace Foundry.Agents.Contracts.Mcp;

public enum ReviewVerdict
{
    Approved,
    ChangesRequested,
    RejectedBlocking,
}
```

```csharp
// ReviewComment.cs
namespace Foundry.Agents.Contracts.Mcp;

public sealed record ReviewComment(string? FilePath, int? Line, string Body);
```

- [ ] **Step 4: `ReviewRequest.cs` and `ReviewResult.cs`**

```csharp
// ReviewRequest.cs
namespace Foundry.Agents.Contracts.Mcp;

public sealed record ReviewRequest(
    string GithubRepo,
    int PrNumber,
    string? ThreadId,
    EffortLevel? Effort);
```

```csharp
// ReviewResult.cs
namespace Foundry.Agents.Contracts.Mcp;

public sealed record ReviewResult(
    string ThreadId,
    ReviewVerdict Verdict,
    IReadOnlyList<ReviewComment> Comments,
    string Summary);
```

- [ ] **Step 5: Build + commit**

```bash
dotnet build src/Foundry.Agents.Contracts
git add src/Foundry.Agents.Contracts/Mcp
git commit -m "Add MCP request/result DTOs for assign_dotnet_task and review_pull_request"
```

---

## Task 4: PersonaLoader + Foundry.Agents.Contracts.Tests project

**Files:**
- Create: `src/Foundry.Agents.Contracts/Personas/PersonaLoader.cs`
- Create: `tests/Foundry.Agents.Contracts.Tests/Foundry.Agents.Contracts.Tests.csproj`
- Create: `tests/Foundry.Agents.Contracts.Tests/Resources/test-persona.md`
- Create: `tests/Foundry.Agents.Contracts.Tests/PersonaLoaderTests.cs`
- Create: `tests/Foundry.Agents.Contracts.Tests/EffortBudgetTests.cs`

- [ ] **Step 1: Spin up the test project**

```bash
dotnet new xunit -n Foundry.Agents.Contracts.Tests -o tests/Foundry.Agents.Contracts.Tests -f net10.0
rm tests/Foundry.Agents.Contracts.Tests/UnitTest1.cs
dotnet sln MSAgentFrameworkFoundry.slnx add tests/Foundry.Agents.Contracts.Tests/Foundry.Agents.Contracts.Tests.csproj
dotnet add tests/Foundry.Agents.Contracts.Tests reference src/Foundry.Agents.Contracts
dotnet add tests/Foundry.Agents.Contracts.Tests package FluentAssertions
```

Edit `Foundry.Agents.Contracts.Tests.csproj` to embed the test persona resource:

```xml
<ItemGroup>
  <EmbeddedResource Include="Resources/test-persona.md" LogicalName="persona.md" />
</ItemGroup>
```

- [ ] **Step 2: Write the test persona resource**

Create `tests/Foundry.Agents.Contracts.Tests/Resources/test-persona.md`:

```markdown
# Test Persona

This is a fixture persona used only by PersonaLoaderTests. Do not consume in production code.
```

- [ ] **Step 3: Write the failing PersonaLoader test**

Create `tests/Foundry.Agents.Contracts.Tests/PersonaLoaderTests.cs`:

```csharp
using System.Reflection;
using FluentAssertions;
using Foundry.Agents.Contracts.Personas;
using Xunit;

namespace Foundry.Agents.Contracts.Tests;

public sealed class PersonaLoaderTests
{
    [Fact]
    public async Task LoadAsync_returns_embedded_persona_markdown_verbatim()
    {
        var content = await PersonaLoader.LoadAsync(Assembly.GetExecutingAssembly());

        content.Should().Contain("# Test Persona");
        content.Should().Contain("PersonaLoaderTests");
    }

    [Fact]
    public async Task LoadAsync_throws_when_resource_missing()
    {
        // Pass an assembly that has no persona.md embedded
        var assemblyWithoutResource = typeof(EffortBudget).Assembly;

        var act = () => PersonaLoader.LoadAsync(assemblyWithoutResource);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*persona.md*not found*");
    }
}
```

- [ ] **Step 4: Run the failing test**

Run: `dotnet test tests/Foundry.Agents.Contracts.Tests --filter PersonaLoaderTests`
Expected: FAIL — `PersonaLoader` does not exist.

- [ ] **Step 5: Implement `PersonaLoader.cs`**

```csharp
using System.Reflection;

namespace Foundry.Agents.Contracts.Personas;

public static class PersonaLoader
{
    private const string ResourceName = "persona.md";

    public static async Task<string> LoadAsync(Assembly assembly, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var fullName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(ResourceName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' not found in assembly '{assembly.GetName().Name}'. " +
                $"Check the project's <EmbeddedResource Include=\"...\" LogicalName=\"persona.md\" /> entry.");

        await using var stream = assembly.GetManifestResourceStream(fullName)
            ?? throw new InvalidOperationException($"Could not open resource stream for '{fullName}'.");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct);
    }
}
```

- [ ] **Step 6: Add the EffortBudget tests**

Create `tests/Foundry.Agents.Contracts.Tests/EffortBudgetTests.cs`:

```csharp
using FluentAssertions;
using Xunit;

namespace Foundry.Agents.Contracts.Tests;

public sealed class EffortBudgetTests
{
    [Theory]
    [InlineData(EffortLevel.Low,    1024,  4096)]
    [InlineData(EffortLevel.Medium, 4096,  8192)]
    [InlineData(EffortLevel.High,   16384, 16384)]
    [InlineData(EffortLevel.Xhigh,  32768, 32768)]
    [InlineData(EffortLevel.Max,    64000, 64000)]
    public void For_maps_each_level_to_the_spec_table(EffortLevel level, int thinking, int output)
    {
        var budget = EffortBudget.For(level);

        budget.ThinkingBudgetTokens.Should().Be(thinking);
        budget.MaxOutputTokens.Should().Be(output);
    }

    [Fact]
    public void For_throws_on_undefined_enum_value()
    {
        var act = () => EffortBudget.For((EffortLevel)999);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
```

- [ ] **Step 7: Run all Contracts tests; expect green**

Run: `dotnet test tests/Foundry.Agents.Contracts.Tests`
Expected: `Passed: 8` (or however many; all green).

- [ ] **Step 8: Commit**

```bash
git add src/Foundry.Agents.Contracts/Personas tests/Foundry.Agents.Contracts.Tests MSAgentFrameworkFoundry.slnx
git commit -m "Add PersonaLoader (embedded resource) and Contracts tests"
```

---

## Task 5: Foundry.Agents.Memory project + AgentThread/AgentRole/ThreadMessage

**Files:**
- Create: `src/Foundry.Agents.Memory/Foundry.Agents.Memory.csproj`
- Create: `src/Foundry.Agents.Memory/AgentRole.cs`
- Create: `src/Foundry.Agents.Memory/ThreadMessage.cs`
- Create: `src/Foundry.Agents.Memory/AgentThread.cs`

- [ ] **Step 1: Create project, reference Contracts, add Cosmos SDK**

```bash
dotnet new classlib -n Foundry.Agents.Memory -o src/Foundry.Agents.Memory -f net10.0
rm src/Foundry.Agents.Memory/Class1.cs
dotnet sln MSAgentFrameworkFoundry.slnx add src/Foundry.Agents.Memory/Foundry.Agents.Memory.csproj
dotnet add src/Foundry.Agents.Memory reference src/Foundry.Agents.Contracts
dotnet add src/Foundry.Agents.Memory package Microsoft.Azure.Cosmos
dotnet add src/Foundry.Agents.Memory package Azure.Identity
```

- [ ] **Step 2: `AgentRole.cs`**

```csharp
using System.Text.Json.Serialization;

namespace Foundry.Agents.Memory;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AgentRole
{
    Developer,
    Reviewer,
}

public static class AgentRoleExtensions
{
    public static string ToPartitionKey(this AgentRole role) => role switch
    {
        AgentRole.Developer => "developer",
        AgentRole.Reviewer  => "reviewer",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };
}
```

- [ ] **Step 3: `ThreadMessage.cs`**

```csharp
namespace Foundry.Agents.Memory;

public sealed record ThreadMessage(string Role, string Content);
```

- [ ] **Step 4: `AgentThread.cs`**

The Cosmos document shape mirrors §5.1 of the spec. Stored as `System.Text.Json` POCO; `ETag` is propagated separately by the store.

```csharp
using System.Text.Json.Serialization;
using Foundry.Agents.Contracts;

namespace Foundry.Agents.Memory;

public sealed class AgentThread
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("agentRole")]
    public required string AgentRole { get; init; }

    [JsonPropertyName("createdUtc")]
    public required DateTimeOffset CreatedUtc { get; init; }

    [JsonPropertyName("updatedUtc")]
    public DateTimeOffset UpdatedUtc { get; set; }

    [JsonPropertyName("ttl")]
    public int Ttl { get; set; } = 604_800;

    [JsonPropertyName("linkedReviewThreadId")]
    public string? LinkedReviewThreadId { get; set; }

    [JsonPropertyName("githubRepo")]
    public string? GithubRepo { get; set; }

    [JsonPropertyName("prNumber")]
    public int? PrNumber { get; set; }

    [JsonPropertyName("reviewRound")]
    public int ReviewRound { get; set; }

    [JsonPropertyName("effort")]
    public EffortLevel? Effort { get; set; }

    [JsonPropertyName("messages")]
    public List<ThreadMessage> Messages { get; set; } = new();

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    /// <summary>Cosmos ETag carried in <see cref="_etag"/>; not part of the public contract, populated by the store.</summary>
    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}
```

- [ ] **Step 5: Build + commit**

```bash
dotnet build src/Foundry.Agents.Memory
git add src/Foundry.Agents.Memory MSAgentFrameworkFoundry.slnx
git commit -m "Add Foundry.Agents.Memory: AgentThread, AgentRole, ThreadMessage"
```

---

## Task 6: Foundry.Agents.Memory.Tests project + Cosmos emulator fixture

**Files:**
- Create: `tests/Foundry.Agents.Memory.Tests/Foundry.Agents.Memory.Tests.csproj`
- Create: `tests/Foundry.Agents.Memory.Tests/CosmosEmulatorFixture.cs`

- [ ] **Step 1: Spin up the test project**

```bash
dotnet new xunit -n Foundry.Agents.Memory.Tests -o tests/Foundry.Agents.Memory.Tests -f net10.0
rm tests/Foundry.Agents.Memory.Tests/UnitTest1.cs
dotnet sln MSAgentFrameworkFoundry.slnx add tests/Foundry.Agents.Memory.Tests/Foundry.Agents.Memory.Tests.csproj
dotnet add tests/Foundry.Agents.Memory.Tests reference src/Foundry.Agents.Memory
dotnet add tests/Foundry.Agents.Memory.Tests package FluentAssertions
dotnet add tests/Foundry.Agents.Memory.Tests package Testcontainers.CosmosDb
```

- [ ] **Step 2: Write the emulator fixture**

`Testcontainers.CosmosDb` boots `mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview` and returns a connection string. The fixture creates the `agentdb` database and `agent-threads` container with the correct PK (`/agentRole`) and default TTL (`604_800`).

Create `CosmosEmulatorFixture.cs`:

```csharp
using Microsoft.Azure.Cosmos;
using Testcontainers.CosmosDb;
using Xunit;

namespace Foundry.Agents.Memory.Tests;

public sealed class CosmosEmulatorFixture : IAsyncLifetime
{
    private readonly CosmosDbContainer _container = new CosmosDbBuilder()
        .WithImage("mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview")
        .Build();

    public CosmosClient Client { get; private set; } = null!;
    public Container ThreadsContainer { get; private set; } = null!;
    public const string DatabaseName  = "agentdb";
    public const string ContainerName = "agent-threads";

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Client = new CosmosClient(_container.GetConnectionString(), new CosmosClientOptions
        {
            ConnectionMode = ConnectionMode.Gateway,
            // The emulator self-signed cert isn't trusted by the test runner.
            HttpClientFactory = () => new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            }),
        });

        var db = await Client.CreateDatabaseIfNotExistsAsync(DatabaseName);
        ThreadsContainer = (await db.Database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(ContainerName, "/agentRole")
            {
                DefaultTimeToLive = 604_800,
            })).Container;
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        await _container.DisposeAsync();
    }
}
```

- [ ] **Step 3: Build + commit**

```bash
dotnet build tests/Foundry.Agents.Memory.Tests
git add tests/Foundry.Agents.Memory.Tests MSAgentFrameworkFoundry.slnx
git commit -m "Add Memory test project with Cosmos linux emulator Testcontainers fixture"
```

---

## Task 7: ICosmosThreadStore + LoadOrCreateAsync (new-thread path)

**Files:**
- Create: `src/Foundry.Agents.Memory/ICosmosThreadStore.cs`
- Create: `src/Foundry.Agents.Memory/CosmosThreadStoreOptions.cs`
- Create: `src/Foundry.Agents.Memory/CosmosThreadStore.cs`
- Create: `tests/Foundry.Agents.Memory.Tests/CosmosThreadStoreTests.cs`

- [ ] **Step 1: Write the failing test for the new-thread case**

```csharp
using FluentAssertions;
using Foundry.Agents.Memory;
using Xunit;

namespace Foundry.Agents.Memory.Tests;

[Collection("Cosmos")]
public sealed class CosmosThreadStoreTests : IClassFixture<CosmosEmulatorFixture>
{
    private readonly CosmosEmulatorFixture _fixture;
    private readonly CosmosThreadStore _store;

    public CosmosThreadStoreTests(CosmosEmulatorFixture fixture)
    {
        _fixture = fixture;
        _store = new CosmosThreadStore(_fixture.ThreadsContainer, TimeProvider.System);
    }

    [Fact]
    public async Task LoadOrCreateAsync_creates_new_thread_with_persona_as_first_system_message()
    {
        var threadId = "01J6Z8K000000000000000001";
        var persona = "# Test Persona\n\nThe rules.";

        var thread = await _store.LoadOrCreateAsync(threadId, AgentRole.Developer, persona, CancellationToken.None);

        thread.Id.Should().Be(threadId);
        thread.AgentRole.Should().Be("developer");
        thread.Messages.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ThreadMessage("system", persona));
        thread.Ttl.Should().Be(604_800);
        thread.ETag.Should().NotBeNullOrEmpty();
    }
}
```

- [ ] **Step 2: Run failing test**

Run: `dotnet test tests/Foundry.Agents.Memory.Tests --filter LoadOrCreateAsync_creates_new_thread`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Define the interface and options**

`ICosmosThreadStore.cs`:

```csharp
namespace Foundry.Agents.Memory;

public interface ICosmosThreadStore
{
    Task<AgentThread> LoadOrCreateAsync(
        string threadId,
        AgentRole role,
        string personaMarkdown,
        CancellationToken ct);

    Task SaveAsync(AgentThread thread, CancellationToken ct);

    Task CompactAsync(string threadId, AgentRole role, string summary, CancellationToken ct);
}
```

`CosmosThreadStoreOptions.cs`:

```csharp
namespace Foundry.Agents.Memory;

public sealed class CosmosThreadStoreOptions
{
    public required string DatabaseName  { get; init; } = "agentdb";
    public required string ContainerName { get; init; } = "agent-threads";
    /// <summary>Default time-to-live for a thread document, in seconds. Refreshed on every write.</summary>
    public int DefaultTtlSeconds { get; init; } = 604_800;
}
```

- [ ] **Step 4: Implement `CosmosThreadStore.LoadOrCreateAsync` (new-thread path)**

```csharp
using Microsoft.Azure.Cosmos;

namespace Foundry.Agents.Memory;

public sealed class CosmosThreadStore : ICosmosThreadStore
{
    private readonly Container _container;
    private readonly TimeProvider _clock;
    private readonly int _defaultTtlSeconds;

    public CosmosThreadStore(Container container, TimeProvider clock, int defaultTtlSeconds = 604_800)
    {
        _container = container ?? throw new ArgumentNullException(nameof(container));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _defaultTtlSeconds = defaultTtlSeconds;
    }

    public async Task<AgentThread> LoadOrCreateAsync(
        string threadId,
        AgentRole role,
        string personaMarkdown,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(personaMarkdown);

        var pk = role.ToPartitionKey();
        try
        {
            var response = await _container.ReadItemAsync<AgentThread>(threadId, new PartitionKey(pk), cancellationToken: ct);
            response.Resource.ETag = response.ETag;
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            var now = _clock.GetUtcNow();
            var thread = new AgentThread
            {
                Id = threadId,
                AgentRole = pk,
                CreatedUtc = now,
                UpdatedUtc = now,
                Ttl = _defaultTtlSeconds,
                Messages = { new ThreadMessage("system", personaMarkdown) },
            };
            var created = await _container.CreateItemAsync(thread, new PartitionKey(pk), cancellationToken: ct);
            created.Resource.ETag = created.ETag;
            return created.Resource;
        }
    }

    public Task SaveAsync(AgentThread thread, CancellationToken ct) =>
        throw new NotImplementedException();

    public Task CompactAsync(string threadId, AgentRole role, string summary, CancellationToken ct) =>
        throw new NotImplementedException();
}
```

- [ ] **Step 5: Run test; expect green**

Run: `dotnet test tests/Foundry.Agents.Memory.Tests --filter LoadOrCreateAsync_creates_new_thread`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Foundry.Agents.Memory tests/Foundry.Agents.Memory.Tests
git commit -m "Add ICosmosThreadStore and LoadOrCreateAsync (new-thread path)"
```

---

## Task 8: LoadOrCreateAsync existing-thread path + serialization round-trip

**Files:**
- Modify: `tests/Foundry.Agents.Memory.Tests/CosmosThreadStoreTests.cs:add test`

- [ ] **Step 1: Write the failing round-trip test**

Append to `CosmosThreadStoreTests`:

```csharp
[Fact]
public async Task LoadOrCreateAsync_returns_existing_thread_with_all_fields_round_tripped()
{
    var threadId = "01J6Z8K000000000000000002";
    var persona  = "# Persona";
    var original = await _store.LoadOrCreateAsync(threadId, AgentRole.Developer, persona, CancellationToken.None);
    original.GithubRepo = "octocat/hello-world";
    original.PrNumber = 142;
    original.ReviewRound = 2;
    original.Effort = Contracts.EffortLevel.Xhigh;
    original.LinkedReviewThreadId = "01J6Z8K000000000000000003";
    original.Messages.Add(new ThreadMessage("user", "add retry to OrderService"));
    await _store.SaveAsync(original, CancellationToken.None);

    var reloaded = await _store.LoadOrCreateAsync(threadId, AgentRole.Developer, persona, CancellationToken.None);

    reloaded.Should().BeEquivalentTo(original, o => o.Excluding(x => x.ETag).Excluding(x => x.UpdatedUtc));
    reloaded.ETag.Should().NotBe(original.ETag);  // SaveAsync produced a new etag
}
```

- [ ] **Step 2: Run; expect failure (SaveAsync NotImplementedException)**

Run: `dotnet test tests/Foundry.Agents.Memory.Tests --filter LoadOrCreateAsync_returns_existing_thread`
Expected: FAIL — `NotImplementedException` from `SaveAsync`.

- [ ] **Step 3: Implement `SaveAsync` (basic upsert, no ETag yet)**

Replace the `SaveAsync` stub in `CosmosThreadStore.cs`:

```csharp
public async Task SaveAsync(AgentThread thread, CancellationToken ct)
{
    ArgumentNullException.ThrowIfNull(thread);

    thread.UpdatedUtc = _clock.GetUtcNow();
    thread.Ttl = _defaultTtlSeconds;  // refresh TTL on every write — see Task 11
    var pk = new PartitionKey(thread.AgentRole);

    var options = new ItemRequestOptions { IfMatchEtag = thread.ETag };
    var response = await _container.UpsertItemAsync(thread, pk, options, ct);
    thread.ETag = response.ETag;
}
```

- [ ] **Step 4: Run test; expect green**

Run: `dotnet test tests/Foundry.Agents.Memory.Tests`
Expected: PASS for both LoadOrCreate tests.

- [ ] **Step 5: Commit**

```bash
git add src/Foundry.Agents.Memory/CosmosThreadStore.cs tests/Foundry.Agents.Memory.Tests/CosmosThreadStoreTests.cs
git commit -m "Implement SaveAsync upsert with ETag round-trip"
```

---

## Task 9: SaveAsync ETag optimistic concurrency (412 path)

- [ ] **Step 1: Write the failing concurrency test**

Append to `CosmosThreadStoreTests`:

```csharp
[Fact]
public async Task SaveAsync_throws_when_etag_does_not_match()
{
    var threadId = "01J6Z8K000000000000000004";
    var persona  = "# Persona";
    var a = await _store.LoadOrCreateAsync(threadId, AgentRole.Developer, persona, CancellationToken.None);
    var b = await _store.LoadOrCreateAsync(threadId, AgentRole.Developer, persona, CancellationToken.None);

    a.Messages.Add(new ThreadMessage("user", "first writer"));
    await _store.SaveAsync(a, CancellationToken.None);                  // succeeds, etag advances

    b.Messages.Add(new ThreadMessage("user", "second writer"));
    var act = () => _store.SaveAsync(b, CancellationToken.None);

    var ex = await act.Should().ThrowAsync<Microsoft.Azure.Cosmos.CosmosException>();
    ex.Which.StatusCode.Should().Be(System.Net.HttpStatusCode.PreconditionFailed);
}
```

- [ ] **Step 2: Run; expect green (existing IfMatchEtag wiring already satisfies it)**

Run: `dotnet test tests/Foundry.Agents.Memory.Tests --filter SaveAsync_throws_when_etag`
Expected: PASS.

If the test fails because `IfMatchEtag` is not enforced when null, harden `SaveAsync` to require a non-empty ETag on inputs that came from a read:

```csharp
if (string.IsNullOrEmpty(thread.ETag))
    throw new InvalidOperationException(
        "AgentThread.ETag must be non-empty before SaveAsync. " +
        "If creating a new thread, use LoadOrCreateAsync instead of SaveAsync.");
```

- [ ] **Step 3: Commit**

```bash
git add tests/Foundry.Agents.Memory.Tests/CosmosThreadStoreTests.cs src/Foundry.Agents.Memory/CosmosThreadStore.cs
git commit -m "Enforce ETag optimistic concurrency on SaveAsync"
```

---

## Task 10: CompactAsync replaces history with persona + summary

- [ ] **Step 1: Write the failing test**

Append to `CosmosThreadStoreTests`:

```csharp
[Fact]
public async Task CompactAsync_replaces_history_with_persona_plus_summary_messages()
{
    var threadId = "01J6Z8K000000000000000005";
    var persona  = "# Developer Persona\nrules.";
    var summary  = "Implemented retry policy in OrderService. PR #142 approved.";

    var t = await _store.LoadOrCreateAsync(threadId, AgentRole.Developer, persona, CancellationToken.None);
    t.Messages.Add(new ThreadMessage("user", "add retry"));
    t.Messages.Add(new ThreadMessage("assistant", "..."));
    t.Messages.Add(new ThreadMessage("user", "looks good?"));
    t.Messages.Add(new ThreadMessage("assistant", "..."));
    await _store.SaveAsync(t, CancellationToken.None);

    await _store.CompactAsync(threadId, AgentRole.Developer, summary, CancellationToken.None);

    var reloaded = await _store.LoadOrCreateAsync(threadId, AgentRole.Developer, persona, CancellationToken.None);
    reloaded.Messages.Should().HaveCount(3);
    reloaded.Messages[0].Should().BeEquivalentTo(new ThreadMessage("system", persona));
    reloaded.Messages[1].Should().BeEquivalentTo(new ThreadMessage("system", $"Prior context summary: {summary}"));
    reloaded.Messages[2].Should().BeEquivalentTo(new ThreadMessage("assistant", summary));
    reloaded.Summary.Should().Be(summary);
}
```

- [ ] **Step 2: Run; expect failure (`NotImplementedException`)**

Run: `dotnet test tests/Foundry.Agents.Memory.Tests --filter CompactAsync_replaces_history`
Expected: FAIL.

- [ ] **Step 3: Implement `CompactAsync`**

Replace the stub in `CosmosThreadStore.cs`:

```csharp
public async Task CompactAsync(string threadId, AgentRole role, string summary, CancellationToken ct)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
    ArgumentException.ThrowIfNullOrWhiteSpace(summary);

    var pk = role.ToPartitionKey();
    var read = await _container.ReadItemAsync<AgentThread>(threadId, new PartitionKey(pk), cancellationToken: ct);
    var thread = read.Resource;
    thread.ETag = read.ETag;

    var persona = thread.Messages.FirstOrDefault()
        ?? throw new InvalidOperationException($"Thread '{threadId}' has no persona message — cannot compact.");
    if (persona.Role != "system")
        throw new InvalidOperationException(
            $"Thread '{threadId}' first message is role '{persona.Role}'; expected 'system' (persona).");

    thread.Messages = new List<ThreadMessage>
    {
        persona,
        new("system", $"Prior context summary: {summary}"),
        new("assistant", summary),
    };
    thread.Summary = summary;

    await SaveAsync(thread, ct);
}
```

- [ ] **Step 4: Run; expect green**

Run: `dotnet test tests/Foundry.Agents.Memory.Tests --filter CompactAsync_replaces_history`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Foundry.Agents.Memory/CosmosThreadStore.cs tests/Foundry.Agents.Memory.Tests/CosmosThreadStoreTests.cs
git commit -m "Implement CompactAsync: replace history with persona + summary"
```

---

## Task 11: TTL refresh on every write

- [ ] **Step 1: Write the failing test**

Append to `CosmosThreadStoreTests`:

```csharp
[Fact]
public async Task SaveAsync_refreshes_ttl_on_each_write()
{
    var threadId = "01J6Z8K000000000000000006";
    var t = await _store.LoadOrCreateAsync(threadId, AgentRole.Developer, "# p", CancellationToken.None);
    t.Ttl = 60;  // simulate prior decay
    await _store.SaveAsync(t, CancellationToken.None);

    var reloaded = await _store.LoadOrCreateAsync(threadId, AgentRole.Developer, "# p", CancellationToken.None);

    reloaded.Ttl.Should().Be(604_800);
}
```

- [ ] **Step 2: Run**

Run: `dotnet test tests/Foundry.Agents.Memory.Tests --filter SaveAsync_refreshes_ttl`
Expected: PASS (already implemented in Task 8 — `thread.Ttl = _defaultTtlSeconds` line). If it doesn't, restore that line.

- [ ] **Step 3: Run the entire Memory test suite**

Run: `dotnet test tests/Foundry.Agents.Memory.Tests`
Expected: all green.

- [ ] **Step 4: Commit**

```bash
git add tests/Foundry.Agents.Memory.Tests/CosmosThreadStoreTests.cs
git commit -m "Lock TTL-refresh-on-write invariant with explicit test"
```

---

## Task 12: Foundry.Agents.TestUtils — fakes and deterministic primitives

**Files:**
- Create: `src/Foundry.Agents.TestUtils/Foundry.Agents.TestUtils.csproj`
- Create: `src/Foundry.Agents.TestUtils/FixedClock.cs`
- Create: `src/Foundry.Agents.TestUtils/DeterministicUlidGenerator.cs`
- Create: `src/Foundry.Agents.TestUtils/FakeGitWorkspace.cs`
- Create: `src/Foundry.Agents.TestUtils/FakeChatClient.cs`

- [ ] **Step 1: Spin up the project, reference shared abstractions**

```bash
dotnet new classlib -n Foundry.Agents.TestUtils -o src/Foundry.Agents.TestUtils -f net10.0
rm src/Foundry.Agents.TestUtils/Class1.cs
dotnet sln MSAgentFrameworkFoundry.slnx add src/Foundry.Agents.TestUtils/Foundry.Agents.TestUtils.csproj
dotnet add src/Foundry.Agents.TestUtils reference src/Foundry.Agents.Contracts
dotnet add src/Foundry.Agents.TestUtils package Microsoft.Extensions.AI.Abstractions
```

`IGitWorkspace` is defined in Plan 2 inside `Foundry.Agents.Developer`. For Plan 1 we forward-declare it here only if Plan 2 hasn't started — in practice the fake stays in TestUtils and the interface is moved into a small `Foundry.Agents.Developer.Abstractions` namespace (Plan 2 T5). For this task we ship only the primitives that don't depend on Plan 2.

- [ ] **Step 2: `FixedClock.cs`**

```csharp
namespace Foundry.Agents.TestUtils;

public sealed class FixedClock : TimeProvider
{
    private DateTimeOffset _now;
    public FixedClock(DateTimeOffset start) => _now = start;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan delta) => _now = _now.Add(delta);
}
```

- [ ] **Step 3: `DeterministicUlidGenerator.cs`**

```csharp
namespace Foundry.Agents.TestUtils;

/// <summary>Deterministic ULID generator: emits 01J6Z8K0000000000000000001, ..002, ..003 in order.</summary>
public sealed class DeterministicUlidGenerator
{
    private int _counter;
    public string Next() => $"01J6Z8K00000000000000000{++_counter:D2}";
}
```

- [ ] **Step 4: `FakeChatClient.cs` — `IChatClient` stand-in**

```csharp
using Microsoft.Extensions.AI;

namespace Foundry.Agents.TestUtils;

/// <summary>Records all GetResponseAsync calls and replays a queued response.</summary>
public sealed class FakeChatClient : IChatClient
{
    private readonly Queue<string> _responses = new();
    public List<(IList<ChatMessage> Messages, ChatOptions? Options)> Calls { get; } = new();
    public void QueueResponse(string text) => _responses.Enqueue(text);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        Calls.Add((list, options));
        var text = _responses.Count > 0
            ? _responses.Dequeue()
            : throw new InvalidOperationException("FakeChatClient.QueueResponse was not called before GetResponseAsync");
        var msg = new ChatMessage(ChatRole.Assistant, text);
        return Task.FromResult(new ChatResponse(msg));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Streaming not exercised in tests");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
```

- [ ] **Step 5: `FakeGitWorkspace.cs` — stub forwarded to Plan 2**

For Plan 1, write a placeholder that compiles. Plan 2 T5 introduces the real `IGitWorkspace` interface and updates this fake to implement it. To avoid a forward reference, we define a tiny abstraction in TestUtils itself, and Plan 2 will move the canonical interface to `Foundry.Agents.Developer`:

```csharp
namespace Foundry.Agents.TestUtils;

/// <summary>
/// Temporary placeholder for Plan 2's IGitWorkspace. Plan 2 T5 will move the interface to
/// Foundry.Agents.Developer.GitWorkspace and re-target this fake.
/// </summary>
public sealed class FakeGitWorkspace
{
    public List<string> Commands { get; } = new();
    public void Record(string command) => Commands.Add(command);
}
```

- [ ] **Step 6: Build + commit**

```bash
dotnet build src/Foundry.Agents.TestUtils
git add src/Foundry.Agents.TestUtils MSAgentFrameworkFoundry.slnx
git commit -m "Add Foundry.Agents.TestUtils: fixed clock, deterministic ULID, fake chat client/git workspace"
```

---

## Task 13: Foundry.Agents.ServiceDefaults — health, OTel, Serilog binding

**Files:**
- Create: `src/Foundry.Agents.ServiceDefaults/Foundry.Agents.ServiceDefaults.csproj`
- Create: `src/Foundry.Agents.ServiceDefaults/ServiceDefaultsExtensions.cs`

- [ ] **Step 1: Spin up the project**

```bash
dotnet new classlib -n Foundry.Agents.ServiceDefaults -o src/Foundry.Agents.ServiceDefaults -f net10.0
rm src/Foundry.Agents.ServiceDefaults/Class1.cs
dotnet sln MSAgentFrameworkFoundry.slnx add src/Foundry.Agents.ServiceDefaults/Foundry.Agents.ServiceDefaults.csproj
dotnet add src/Foundry.Agents.ServiceDefaults package Microsoft.AspNetCore.OpenApi
dotnet add src/Foundry.Agents.ServiceDefaults package Microsoft.Extensions.Hosting
dotnet add src/Foundry.Agents.ServiceDefaults package Serilog.AspNetCore
dotnet add src/Foundry.Agents.ServiceDefaults package Serilog.Settings.Configuration
dotnet add src/Foundry.Agents.ServiceDefaults package Serilog.Sinks.Console
dotnet add src/Foundry.Agents.ServiceDefaults package OpenTelemetry.Extensions.Hosting
dotnet add src/Foundry.Agents.ServiceDefaults package OpenTelemetry.Instrumentation.AspNetCore
dotnet add src/Foundry.Agents.ServiceDefaults package OpenTelemetry.Instrumentation.Http
dotnet add src/Foundry.Agents.ServiceDefaults package OpenTelemetry.Exporter.OpenTelemetryProtocol
```

Because this project consumes `WebApplicationBuilder`, set `<Sdk Name="Microsoft.NET.Sdk.Web" />` is *not* needed; we use a regular classlib and add a framework reference to `Microsoft.AspNetCore.App` in the csproj:

```xml
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
</ItemGroup>
```

- [ ] **Step 2: Write `ServiceDefaultsExtensions.cs`**

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

namespace Foundry.Agents.ServiceDefaults;

public static class ServiceDefaultsExtensions
{
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        // D-14: Serilog from configuration only; Console JSON sink only.
        builder.Services.AddSerilog((services, cfg) => cfg
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services));

        builder.Services.AddProblemDetails();

        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live" });

        builder.Services
            .AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(builder.Environment.ApplicationName))
            .WithTracing(t => t
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSource("Foundry.Agents.*")
                .AddOtlpExporter())
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter());

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live"),
        });
        app.MapHealthChecks("/health/ready");
        return app;
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/Foundry.Agents.ServiceDefaults`
Expected: `Build succeeded`. (No tests — `ServiceDefaults` is covered transitively by Plan 2's service tests.)

- [ ] **Step 4: Commit**

```bash
git add src/Foundry.Agents.ServiceDefaults MSAgentFrameworkFoundry.slnx
git commit -m "Add Foundry.Agents.ServiceDefaults: health, OTel, Serilog binding"
```

---

## Final verification

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build MSAgentFrameworkFoundry.slnx`
Expected: All 6 projects build; zero warnings (because `TreatWarningsAsErrors=true`).

- [ ] **Step 2: Run all Plan-1 tests**

Run: `dotnet test MSAgentFrameworkFoundry.slnx`
Expected:
- `Foundry.Agents.Contracts.Tests`: all green (~8 tests)
- `Foundry.Agents.Memory.Tests`: all green (~5 tests, slower due to Cosmos emulator startup)

- [ ] **Step 3: Tag the milestone**

```bash
git tag plan-1-libraries-complete
```

Plan 1 is done when:
- 6 projects compile under `TreatWarningsAsErrors=true`.
- All Contracts.Tests pass.
- All Memory.Tests pass against the Cosmos linux emulator.
- No `NotImplementedException` stubs remain in shipped code.

Proceed to [Plan 2: Services](implementation-plan-2-services.md).
