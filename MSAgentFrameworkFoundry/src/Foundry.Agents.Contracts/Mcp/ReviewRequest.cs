namespace Foundry.Agents.Contracts.Mcp;

public sealed record ReviewRequest(
    string GithubRepo,
    int PrNumber,
    string? ThreadId,
    EffortLevel? Effort);
