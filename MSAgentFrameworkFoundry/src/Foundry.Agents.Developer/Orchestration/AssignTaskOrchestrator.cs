using Foundry.Agents.Contracts;
using Foundry.Agents.Contracts.Mcp;
using Foundry.Agents.Contracts.Personas;
using Foundry.Agents.Developer.GitHubMcp;
using Foundry.Agents.Developer.GitWorkspace;
using Foundry.Agents.Developer.Reviewer;
using Foundry.Agents.Memory;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Foundry.Agents.Developer.Orchestration;

public sealed class AssignTaskOrchestrator : IAssignTaskOrchestrator
{
    private readonly ICosmosThreadStore _store;
    private readonly IGitWorkspace _git;
    private readonly IGitHubMcpClient _github;
    private readonly IReviewerMcpClient _reviewer;
    private readonly IChatClientFactory _chatFactory;
    private readonly EffortResolver _effort;
    private readonly OrchestratorOptions _options;
    private readonly IUlidGenerator _ulids;
    private readonly ILogger<AssignTaskOrchestrator> _logger;

    public AssignTaskOrchestrator(
        ICosmosThreadStore store,
        IGitWorkspace git,
        IGitHubMcpClient github,
        IReviewerMcpClient reviewer,
        IChatClientFactory chatClientFactory,
        EffortResolver effortResolver,
        OrchestratorOptions options,
        IUlidGenerator ulids,
        ILogger<AssignTaskOrchestrator> logger)
    {
        _store = store;
        _git = git;
        _github = github;
        _reviewer = reviewer;
        _chatFactory = chatClientFactory;
        _effort = effortResolver;
        _options = options;
        _ulids = ulids;
        _logger = logger;
    }

    public async Task<AssignTaskResult> HandleAsync(AssignTaskRequest request, CancellationToken ct)
    {
        var threadId = request.ThreadId ?? _ulids.Next();
        var persona = await PersonaLoader.LoadAsync(typeof(AssignTaskOrchestrator).Assembly, ct);
        var thread = await _store.LoadOrCreateAsync(threadId, AgentRole.Developer, persona, ct);
        thread.GithubRepo = request.GithubRepo;
        var effort = _effort.Resolve(request.Effort, thread.Effort);
        thread.Effort = effort;

        var workDir = Path.Combine(_options.WorkspaceRoot, threadId);
        var cloneUrl = $"https://github.com/{request.GithubRepo}.git";
        var clone = await _git.CloneAsync(new CloneRequest(cloneUrl, workDir, Branch: null), ct);
        if (clone.ExitCode != 0)
            return new AssignTaskResult(threadId, AssignTaskStatus.Error, null, $"clone failed: {clone.StdErr}", Array.Empty<string>());

        var branch = BranchNameFor(threadId, request.TaskDescription);
        var checkoutR = await _git.CheckoutNewBranchAsync(workDir, branch, ct);
        if (checkoutR.ExitCode != 0)
            return new AssignTaskResult(threadId, AssignTaskStatus.Error, null, $"checkout failed: {checkoutR.StdErr}", Array.Empty<string>());

        thread.Messages.Add(new ThreadMessage("user", request.TaskDescription));
        var chatClient = _chatFactory.Create();
        var chatOptions = ChatClientFactory.ChatOptionsFor(effort);
        var chatMessages = thread.Messages
            .Select(m => new ChatMessage(new ChatRole(m.Role), m.Content))
            .ToList();
        var response = await chatClient.GetResponseAsync(chatMessages, chatOptions, ct);
        thread.Messages.Add(new ThreadMessage("assistant", response.Text));

        var restore = await _git.DotnetRestoreAsync(workDir, _options.SolutionPath, ct);
        if (restore.ExitCode != 0)
        {
            await _store.SaveAsync(thread, ct);
            return new AssignTaskResult(threadId, AssignTaskStatus.BuildFailed, null, restore.StdErr, Array.Empty<string>());
        }

        var build = await _git.DotnetBuildAsync(workDir, _options.SolutionPath, ct);
        if (build.ExitCode != 0)
        {
            await _store.SaveAsync(thread, ct);
            return new AssignTaskResult(threadId, AssignTaskStatus.BuildFailed, null, build.StdErr, Array.Empty<string>());
        }

        var test = await _git.DotnetTestAsync(workDir, _options.SolutionPath, ct);
        if (test.ExitCode != 0)
        {
            await _store.SaveAsync(thread, ct);
            return new AssignTaskResult(threadId, AssignTaskStatus.BuildFailed, null, test.StdErr, Array.Empty<string>());
        }

        await _store.SaveAsync(thread, ct);
        // Push + PR + review loop added in Tasks 9 and 11.
        return new AssignTaskResult(threadId, AssignTaskStatus.Error, null, "push/PR/review not yet wired", Array.Empty<string>());
    }

    private static string BranchNameFor(string threadId, string taskDescription)
    {
        if (!string.IsNullOrWhiteSpace(threadId) && threadId.Length >= 8)
            return $"agent/{threadId[..8].ToLowerInvariant()}";

        var slug = new string(taskDescription.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        slug = slug.Trim('-');
        if (slug.Length > 28)
            slug = slug[..28].TrimEnd('-');

        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{threadId}|{taskDescription}"));
        var shortHash = Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
        return $"agent/{slug}-{shortHash}";
    }
}
