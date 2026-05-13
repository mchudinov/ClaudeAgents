namespace Foundry.Agents.Developer.GitWorkspace;

public sealed record CloneRequest(string RepoUrl, string DestinationPath, string? Branch);
public sealed record ShellResult(int ExitCode, string StdOut, string StdErr);

public interface IGitWorkspace
{
    Task<ShellResult> CloneAsync(CloneRequest request, CancellationToken ct);
    Task<ShellResult> CheckoutNewBranchAsync(string repoPath, string branch, CancellationToken ct);
    Task<ShellResult> CommitAllAsync(string repoPath, string message, CancellationToken ct);
    Task<ShellResult> PushAsync(string repoPath, string branch, CancellationToken ct);
    Task<ShellResult> DotnetRestoreAsync(string repoPath, string? solutionPath, CancellationToken ct);
    Task<ShellResult> DotnetBuildAsync(string repoPath, string? solutionPath, CancellationToken ct);
    Task<ShellResult> DotnetTestAsync(string repoPath, string? solutionPath, CancellationToken ct);
}
