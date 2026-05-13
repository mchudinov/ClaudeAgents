namespace Foundry.Agents.Developer.Orchestration;

public sealed class OrchestratorOptions
{
    public required string WorkspaceRoot { get; init; }
    public required int MaxReviewRounds { get; init; }
    public required string DefaultBranch { get; init; }
    public string? SolutionPath { get; init; }
}
