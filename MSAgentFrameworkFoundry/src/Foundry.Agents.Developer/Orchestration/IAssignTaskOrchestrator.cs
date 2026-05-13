using Foundry.Agents.Contracts.Mcp;

namespace Foundry.Agents.Developer.Orchestration;

public interface IAssignTaskOrchestrator
{
    Task<AssignTaskResult> HandleAsync(AssignTaskRequest request, CancellationToken ct);
}
