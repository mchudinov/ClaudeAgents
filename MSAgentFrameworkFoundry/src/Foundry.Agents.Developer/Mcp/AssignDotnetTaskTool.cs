using System.ComponentModel;
using Foundry.Agents.Contracts.Mcp;
using Foundry.Agents.Developer.Orchestration;
using ModelContextProtocol.Server;

namespace Foundry.Agents.Developer.Mcp;

[McpServerToolType]
public sealed class AssignDotnetTaskTool
{
    private readonly IAssignTaskOrchestrator _orchestrator;
    public AssignDotnetTaskTool(IAssignTaskOrchestrator orchestrator) => _orchestrator = orchestrator;

    [McpServerTool(Name = "assign_dotnet_task"), Description(
        "Assign a .NET 10 C# coding task to the Developer agent. The agent clones the repo, implements the change, opens a PR, and asks the Reviewer to approve. Returns ThreadId, Status, PrUrl, Summary, UnresolvedComments.")]
    public async Task<AssignTaskResult> HandleAsync(AssignTaskRequest request, CancellationToken ct)
    {
        Validate(request);
        return await _orchestrator.HandleAsync(request, ct);
    }

    private static void Validate(AssignTaskRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.GithubRepo))
            throw new ArgumentException("GithubRepo is required.", nameof(r));
        if (!r.GithubRepo.Contains('/', StringComparison.Ordinal) || r.GithubRepo.Split('/').Length != 2)
            throw new ArgumentException("GithubRepo must be in 'owner/repo' format.", nameof(r));
        if (string.IsNullOrWhiteSpace(r.TaskDescription))
            throw new ArgumentException("TaskDescription is required.", nameof(r));
    }
}
