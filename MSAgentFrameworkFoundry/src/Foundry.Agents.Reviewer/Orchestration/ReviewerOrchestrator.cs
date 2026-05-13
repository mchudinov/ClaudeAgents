using Foundry.Agents.Contracts;
using Foundry.Agents.Contracts.Chat;
using Foundry.Agents.Contracts.Mcp;
using Foundry.Agents.Contracts.Personas;
using Foundry.Agents.Memory;
using Foundry.Agents.Reviewer.GitHubMcp;
using Foundry.Agents.Reviewer.Mcp;
using Foundry.Agents.Reviewer.Verdict;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Foundry.Agents.Reviewer.Orchestration;

public sealed class ReviewerOrchestrator : IReviewerOrchestrator
{
    private readonly ICosmosThreadStore _store;
    private readonly IReviewerGitHubMcpClient _github;
    private readonly IChatClientFactory _chatFactory;
    private readonly EffortResolver _effort;
    private readonly ILogger<ReviewerOrchestrator> _logger;
    private int _ulidCounter;

    public ReviewerOrchestrator(
        ICosmosThreadStore store,
        IReviewerGitHubMcpClient github,
        IChatClientFactory chatFactory,
        EffortResolver effort,
        ILogger<ReviewerOrchestrator> logger)
    {
        _store = store;
        _github = github;
        _chatFactory = chatFactory;
        _effort = effort;
        _logger = logger;
    }

    public async Task<ReviewResult> HandleAsync(ReviewRequest request, CancellationToken ct)
    {
        var threadId = request.ThreadId ?? GenerateId();
        var persona = await PersonaLoader.LoadAsync(typeof(ReviewerOrchestrator).Assembly, ct);
        var thread = await _store.LoadOrCreateAsync(threadId, AgentRole.Reviewer, persona, ct);
        var effort = _effort.Resolve(request.Effort, thread.Effort);
        thread.Effort = effort;
        thread.GithubRepo = request.GithubRepo;
        thread.PrNumber = request.PrNumber;

        var diff = await _github.GetPullRequestDiffAsync(request.GithubRepo, request.PrNumber, ct);
        var files = await _github.GetPullRequestFilesAsync(request.GithubRepo, request.PrNumber, ct);

        var prompt =
            $"Review PR #{request.PrNumber} on {request.GithubRepo}. " +
            $"Files changed: {string.Join(", ", files.Select(f => f.Path))}.\n\n" +
            $"Unified diff:\n{diff.UnifiedDiff}\n\n" +
            "Output JSON: { \"verdict\": \"APPROVE|REQUEST_CHANGES|REJECT\", \"summary\": \"...\", " +
            "\"comments\": [{\"path\":\"...\",\"line\":1,\"body\":\"...\"}] }";
        thread.Messages.Add(new ThreadMessage("user", prompt));

        var client = _chatFactory.Create();
        var chatOptions = new ChatOptions { MaxOutputTokens = EffortBudget.For(effort).MaxOutputTokens };
        var resp = await client.GetResponseAsync(
            thread.Messages.Select(m => new ChatMessage(new ChatRole(m.Role), m.Content)).ToList(),
            chatOptions, ct);
        thread.Messages.Add(new ThreadMessage("assistant", resp.Text));

        var json = System.Text.Json.JsonDocument.Parse(ExtractJson(resp.Text));
        var verdict = VerdictMapper.FromModelOutput(json.RootElement.GetProperty("verdict").GetString()!);
        var summary = json.RootElement.GetProperty("summary").GetString()!;
        var comments = new List<ReviewComment>();
        if (json.RootElement.TryGetProperty("comments", out var arr))
            foreach (var c in arr.EnumerateArray())
                comments.Add(new ReviewComment(
                    c.TryGetProperty("path", out var p) ? p.GetString() : null,
                    c.TryGetProperty("line", out var l) ? l.GetInt32() : null,
                    c.GetProperty("body").GetString()!));

        var pending = await _github.CreatePendingReviewAsync(request.GithubRepo, request.PrNumber, ct);
        foreach (var c in comments)
            await _github.AddCommentToPendingReviewAsync(request.GithubRepo, request.PrNumber, pending,
                new PendingReviewComment(c.FilePath, c.Line, c.Body), ct);
        var githubVerdict = verdict == ReviewVerdict.Approved ? "APPROVE" : "REQUEST_CHANGES";
        await _github.SubmitReviewAsync(request.GithubRepo, request.PrNumber, pending, githubVerdict, summary, ct);

        await _store.SaveAsync(thread, ct);
        return new ReviewResult(threadId, verdict, comments, summary);
    }

    private string GenerateId() => $"01J6Z8R00000000000000000{Interlocked.Increment(ref _ulidCounter):D2}";

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end < 0) throw new InvalidOperationException($"Model did not return JSON: {text}");
        return text[start..(end + 1)];
    }
}
