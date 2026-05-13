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
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Foundry.Agents.Developer.Tests;

public sealed class AssignTaskOrchestratorTests
{
    [Fact]
    public async Task Returns_BuildFailed_when_dotnet_build_exits_nonzero_and_does_not_push_or_open_pr()
    {
        var git = new FakeGitWorkspace();
        git.Responses.Enqueue(new ShellResult(0, "", ""));                    // clone
        git.Responses.Enqueue(new ShellResult(0, "", ""));                    // checkout -b
        git.Responses.Enqueue(new ShellResult(0, "", ""));                    // dotnet restore
        git.Responses.Enqueue(new ShellResult(1, "", "build error CS1002"));  // dotnet build fails

        var store = Substitute.For<ICosmosThreadStore>();
        store.LoadOrCreateAsync(default!, default, default!, default)
            .ReturnsForAnyArgs(ci => new AgentThread
            {
                Id = ci.ArgAt<string>(0),
                AgentRole = "developer",
                CreatedUtc = DateTimeOffset.UtcNow,
                Messages = { new ThreadMessage("system", "# Developer Persona") },
                ETag = "etag-1",
            });

        var chat = new FakeChatClient();
        chat.QueueResponse("done");

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

    [Fact]
    public async Task Pushes_branch_and_creates_PR_when_build_and_test_pass()
    {
        var git = new FakeGitWorkspace();
        git.Responses.Enqueue(new ShellResult(0, "", "")); // clone
        git.Responses.Enqueue(new ShellResult(0, "", "")); // checkout -b
        git.Responses.Enqueue(new ShellResult(0, "", "")); // dotnet restore
        git.Responses.Enqueue(new ShellResult(0, "", "")); // dotnet build --no-restore
        git.Responses.Enqueue(new ShellResult(0, "", "")); // dotnet test --no-build
        git.Responses.Enqueue(new ShellResult(0, "", "")); // commit
        git.Responses.Enqueue(new ShellResult(0, "", "")); // push

        var store = Substitute.For<ICosmosThreadStore>();
        store.LoadOrCreateAsync(default!, default, default!, default).ReturnsForAnyArgs(ci => new AgentThread
        {
            Id = ci.ArgAt<string>(0), AgentRole = "developer",
            CreatedUtc = DateTimeOffset.UtcNow, Messages = { new ThreadMessage("system", "# p") }, ETag = "e",
        });

        var chat = new FakeChatClient(); chat.QueueResponse("done");
        var gh = Substitute.For<IGitHubMcpClient>();
        gh.CreatePullRequestAsync(default!, default).ReturnsForAnyArgs(
            new CreatePrResponse(142, "https://github.com/octocat/hello/pull/142"));
        var reviewer = Substitute.For<IReviewerMcpClient>();
        reviewer.ReviewPullRequestAsync(default!, default).ThrowsForAnyArgs(
            new HttpRequestException("reviewer down"));

        var orchestrator = new AssignTaskOrchestrator(
            store, git, gh, reviewer,
            new TestChatClientFactory(chat),
            new EffortResolver(EffortLevel.Xhigh),
            new OrchestratorOptions { WorkspaceRoot = "/tmp/work", MaxReviewRounds = 3, DefaultBranch = "main" },
            new DeterministicUlidGenerator(),
            NullLogger<AssignTaskOrchestrator>.Instance);

        var result = await orchestrator.HandleAsync(
            new AssignTaskRequest("octocat/hello", "fix bug", null, null), default);

        await gh.Received(1).CreatePullRequestAsync(
            Arg.Is<CreatePrRequest>(r => r.Repo == "octocat/hello" && r.Base == "main"),
            Arg.Any<CancellationToken>());
        result.PrUrl.Should().Be("https://github.com/octocat/hello/pull/142");
    }
}

internal sealed class TestChatClientFactory : IChatClientFactory
{
    private readonly Microsoft.Extensions.AI.IChatClient _client;
    public TestChatClientFactory(Microsoft.Extensions.AI.IChatClient client) => _client = client;
    public Microsoft.Extensions.AI.IChatClient Create() => _client;
}
