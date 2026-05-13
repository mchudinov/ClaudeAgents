using FluentAssertions;
using Foundry.Agents.Reviewer.GitHubMcp;
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
        var client = new ReviewerGitHubMcpClient();
        var act = () => client.SubmitReviewAsync("o/r", 1, "pid", "MERGE", "ship it", default);
        act.Should().ThrowAsync<ArgumentException>().WithMessage("*Merge is not allowed*");
    }
}
