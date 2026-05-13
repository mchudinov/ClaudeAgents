using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Foundry.Agents.Reviewer.GitHubMcp;

public sealed class ReviewerGitHubMcpClient : IReviewerGitHubMcpClient
{
    private readonly McpClient _client;

    public ReviewerGitHubMcpClient(McpClient client) => _client = client;

    internal ReviewerGitHubMcpClient() => _client = null!;

    public static async Task<ReviewerGitHubMcpClient> CreateAsync(Uri endpoint, string? bearerToken, CancellationToken ct)
    {
        var options = new HttpClientTransportOptions
        {
            Endpoint = endpoint,
            AdditionalHeaders = bearerToken is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string> { ["Authorization"] = $"Bearer {bearerToken}" },
        };
        var transport = new HttpClientTransport(options);
        var client = await McpClient.CreateAsync(transport, cancellationToken: ct);
        return new ReviewerGitHubMcpClient(client);
    }

    private async Task<string> CallAsync(string tool, Dictionary<string, object?> args, CancellationToken ct)
    {
        var result = await _client.CallToolAsync(tool, args, cancellationToken: ct);
        return result.Content.OfType<TextContentBlock>().First().Text;
    }

    private static (string owner, string repo) Split(string repo)
    {
        var parts = repo.Split('/');
        return (parts[0], parts[1]);
    }

    public async Task<PrDiff> GetPullRequestDiffAsync(string repo, int prNumber, CancellationToken ct)
    {
        var (o, r) = Split(repo);
        var json = await CallAsync("get_pull_request_diff",
            new() { ["owner"] = o, ["repo"] = r, ["pullNumber"] = prNumber }, ct);
        using var d = System.Text.Json.JsonDocument.Parse(json);
        return new PrDiff(
            d.RootElement.GetProperty("diff").GetString()!,
            d.RootElement.GetProperty("head_sha").GetString()!);
    }

    public async Task<IReadOnlyList<PrFile>> GetPullRequestFilesAsync(string repo, int prNumber, CancellationToken ct)
    {
        var (o, r) = Split(repo);
        var json = await CallAsync("get_pull_request_files",
            new() { ["owner"] = o, ["repo"] = r, ["pullNumber"] = prNumber }, ct);
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
            new() { ["owner"] = o, ["repo"] = r, ["pullNumber"] = prNumber }, ct);
        using var d = System.Text.Json.JsonDocument.Parse(json);
        return d.RootElement.GetProperty("id").GetString()!;
    }

    public Task AddCommentToPendingReviewAsync(string repo, int prNumber, string pendingReviewId, PendingReviewComment comment, CancellationToken ct)
    {
        var (o, r) = Split(repo);
        return CallAsync("add_comment_to_pending_review", new()
        {
            ["owner"] = o,
            ["repo"] = r,
            ["pullNumber"] = prNumber,
            ["pendingReviewId"] = pendingReviewId,
            ["path"] = comment.FilePath,
            ["line"] = comment.Line,
            ["body"] = comment.Body,
        }, ct);
    }

    public Task SubmitReviewAsync(string repo, int prNumber, string pendingReviewId, string verdict, string body, CancellationToken ct)
    {
        var (o, r) = Split(repo);
        if (verdict is not ("APPROVE" or "REQUEST_CHANGES"))
            throw new ArgumentException(
                $"Verdict must be APPROVE or REQUEST_CHANGES; got '{verdict}'. Merge is not allowed.",
                nameof(verdict));
        return CallAsync("submit_pending_pull_request_review", new()
        {
            ["owner"] = o,
            ["repo"] = r,
            ["pullNumber"] = prNumber,
            ["pendingReviewId"] = pendingReviewId,
            ["event"] = verdict,
            ["body"] = body,
        }, ct);
    }
}
