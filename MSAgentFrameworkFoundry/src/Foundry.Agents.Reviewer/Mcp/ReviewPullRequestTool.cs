using System.ComponentModel;
using Foundry.Agents.Contracts.Mcp;
using ModelContextProtocol.Server;

namespace Foundry.Agents.Reviewer.Mcp;

public interface IReviewerOrchestrator
{
    Task<ReviewResult> HandleAsync(ReviewRequest request, CancellationToken ct);
}

[McpServerToolType]
public sealed class ReviewPullRequestTool
{
    private readonly IReviewerOrchestrator _orchestrator;

    public ReviewPullRequestTool(IReviewerOrchestrator orchestrator) => _orchestrator = orchestrator;

    [McpServerTool(Name = "review_pull_request"), Description(
        "Review a GitHub pull request. Returns ThreadId, Verdict (Approved | ChangesRequested | RejectedBlocking), itemized Comments, and a Summary. Never merges.")]
    public Task<ReviewResult> HandleAsync(ReviewRequest request, CancellationToken ct) =>
        _orchestrator.HandleAsync(request, ct);
}
