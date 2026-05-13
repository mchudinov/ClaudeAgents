namespace Foundry.Agents.Contracts.Mcp;

public sealed record AssignTaskResult(
    string ThreadId,
    AssignTaskStatus Status,
    string? PrUrl,
    string? Summary,
    IReadOnlyList<string> UnresolvedComments);
