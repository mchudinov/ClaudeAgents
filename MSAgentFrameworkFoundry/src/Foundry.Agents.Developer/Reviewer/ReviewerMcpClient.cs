using Foundry.Agents.Contracts.Mcp;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Polly;

namespace Foundry.Agents.Developer.Reviewer;

public sealed class ReviewerMcpClient : IReviewerMcpClient, IAsyncDisposable
{
    private readonly McpClient _client;
    private readonly ResiliencePipeline _retry;

    public ReviewerMcpClient(McpClient client, ResiliencePipeline retry)
    {
        _client = client;
        _retry = retry;
    }

    public static async Task<ReviewerMcpClient> CreateAsync(Uri endpoint, CancellationToken ct)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions { Endpoint = endpoint });
        var client = await McpClient.CreateAsync(transport, cancellationToken: ct);
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
