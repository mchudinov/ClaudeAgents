namespace Foundry.Agents.Contracts.Mcp;

public sealed record ReviewResult(
    string ThreadId,
    ReviewVerdict Verdict,
    IReadOnlyList<ReviewComment> Comments,
    string Summary);
