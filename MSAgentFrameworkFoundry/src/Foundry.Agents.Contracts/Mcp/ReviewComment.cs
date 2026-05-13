namespace Foundry.Agents.Contracts.Mcp;

public sealed record ReviewComment(string? FilePath, int? Line, string Body);
