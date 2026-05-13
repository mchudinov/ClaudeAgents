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
    /// <summary>Submits the pending review. Verdict must be APPROVE or REQUEST_CHANGES — never MERGE.</summary>
    Task SubmitReviewAsync(string repo, int prNumber, string pendingReviewId, string verdict, string body, CancellationToken ct);
}
