using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Foundry.Agents.Developer.GitHubMcp;

public sealed class GitHubMcpClient : IGitHubMcpClient, IAsyncDisposable
{
    private readonly McpClient _client;

    private GitHubMcpClient(McpClient client) => _client = client;

    public static async Task<GitHubMcpClient> CreateAsync(Uri endpoint, string? bearerToken, CancellationToken ct)
    {
        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = endpoint,
            AdditionalHeaders = bearerToken is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string> { ["Authorization"] = $"Bearer {bearerToken}" },
        };
        var transport = new HttpClientTransport(transportOptions);
        var client = await McpClient.CreateAsync(transport, cancellationToken: ct);
        return new GitHubMcpClient(client);
    }

    public async Task<CreatePrResponse> CreatePullRequestAsync(CreatePrRequest request, CancellationToken ct)
    {
        var parts = request.Repo.Split('/');
        var args = new Dictionary<string, object?>
        {
            ["owner"] = parts[0],
            ["repo"]  = parts[1],
            ["head"]  = request.Head,
            ["base"]  = request.Base,
            ["title"] = request.Title,
            ["body"]  = request.Body,
        };
        var result = await _client.CallToolAsync("create_pull_request", args, cancellationToken: ct);
        var payload = result.Content
            .OfType<TextContentBlock>()
            .First()
            .Text;
        using var doc = System.Text.Json.JsonDocument.Parse(payload);
        return new CreatePrResponse(
            doc.RootElement.GetProperty("number").GetInt32(),
            doc.RootElement.GetProperty("html_url").GetString()!);
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
