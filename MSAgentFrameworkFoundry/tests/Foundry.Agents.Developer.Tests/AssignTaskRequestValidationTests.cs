using FluentAssertions;
using Foundry.Agents.Contracts.Mcp;
using Foundry.Agents.Developer.Mcp;
using Foundry.Agents.Developer.Orchestration;
using NSubstitute;
using Xunit;

namespace Foundry.Agents.Developer.Tests;

public sealed class AssignTaskRequestValidationTests
{
    private static AssignDotnetTaskTool MakeTool() =>
        new(Substitute.For<IAssignTaskOrchestrator>());

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
