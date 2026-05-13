using Foundry.Agents.Contracts.Mcp;

namespace Foundry.Agents.Developer.Reviewer;

public interface IReviewerMcpClient
{
    Task<ReviewResult> ReviewPullRequestAsync(ReviewRequest request, CancellationToken ct);
}
