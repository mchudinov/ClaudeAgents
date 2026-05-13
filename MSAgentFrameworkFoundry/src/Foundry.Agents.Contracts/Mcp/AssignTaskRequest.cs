namespace Foundry.Agents.Contracts.Mcp;

public sealed record AssignTaskRequest(
    string GithubRepo,
    string TaskDescription,
    string? ThreadId,
    EffortLevel? Effort);
