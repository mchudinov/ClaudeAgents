namespace Foundry.Agents.Developer.GitHubMcp;

public sealed record CreatePrRequest(string Repo, string Head, string Base, string Title, string Body);
public sealed record CreatePrResponse(int Number, string HtmlUrl);

public interface IGitHubMcpClient
{
    Task<CreatePrResponse> CreatePullRequestAsync(CreatePrRequest request, CancellationToken ct);
}
