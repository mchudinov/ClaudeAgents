using FluentAssertions;
using Foundry.Agents.Contracts;
using Foundry.Agents.Contracts.Chat;
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

        var result = await orch.HandleAsync(
            new AssignTaskRequest("octocat/hello", "fix bug", null, null), default);

        result.Status.Should().Be(AssignTaskStatus.ReviewFailed);
        await reviewer.Received(3).ReviewPullRequestAsync(
            Arg.Any<ReviewRequest>(), Arg.Any<CancellationToken>());
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

        var result = await orch.HandleAsync(
            new AssignTaskRequest("octocat/hello", "fix bug", null, null), default);

        result.Status.Should().Be(AssignTaskStatus.ReviewFailed);
        await reviewer.Received(1).ReviewPullRequestAsync(
            Arg.Any<ReviewRequest>(), Arg.Any<CancellationToken>());
    }

    private static (FakeGitWorkspace, ICosmosThreadStore, IGitHubMcpClient) MakeHappyDeps()
    {
        var git = new FakeGitWorkspace();
        for (int i = 0; i < 30; i++) git.Responses.Enqueue(new ShellResult(0, "", ""));
        var store = Substitute.For<ICosmosThreadStore>();
        store.LoadOrCreateAsync(default!, default, default!, default).ReturnsForAnyArgs(ci => new AgentThread
        {
            Id = ci.ArgAt<string>(0), AgentRole = "developer",
            CreatedUtc = DateTimeOffset.UtcNow,
            Messages = { new ThreadMessage("system", "# p") }, ETag = "e",
        });
        var gh = Substitute.For<IGitHubMcpClient>();
        gh.CreatePullRequestAsync(default!, default).ReturnsForAnyArgs(
            new CreatePrResponse(142, "https://example/142"));
        return (git, store, gh);
    }
}
