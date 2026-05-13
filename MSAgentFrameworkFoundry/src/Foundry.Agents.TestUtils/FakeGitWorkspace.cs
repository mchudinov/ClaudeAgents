using Foundry.Agents.Developer.GitWorkspace;

namespace Foundry.Agents.TestUtils;

public sealed class FakeGitWorkspace : IGitWorkspace
{
    public List<string> Commands { get; } = new();
    public Queue<ShellResult> Responses { get; } = new();

    private ShellResult Pop() =>
        Responses.Count > 0 ? Responses.Dequeue() : new ShellResult(0, "", "");

    public Task<ShellResult> CloneAsync(CloneRequest request, CancellationToken ct) { Commands.Add($"clone {request.RepoUrl} {request.Branch ?? "<default>"} -> {request.DestinationPath}"); return Task.FromResult(Pop()); }
    public Task<ShellResult> CheckoutNewBranchAsync(string repoPath, string branch, CancellationToken ct) { Commands.Add($"checkout -b {branch} in {repoPath}"); return Task.FromResult(Pop()); }
    public Task<ShellResult> CommitAllAsync(string repoPath, string message, CancellationToken ct) { Commands.Add($"commit -m {message} in {repoPath}"); return Task.FromResult(Pop()); }
    public Task<ShellResult> PushAsync(string repoPath, string branch, CancellationToken ct) { Commands.Add($"push {branch} in {repoPath}"); return Task.FromResult(Pop()); }
    public Task<ShellResult> DotnetRestoreAsync(string repoPath, string? solutionPath, CancellationToken ct) { Commands.Add($"dotnet restore {solutionPath ?? "<auto>"} in {repoPath}"); return Task.FromResult(Pop()); }
    public Task<ShellResult> DotnetBuildAsync(string repoPath, string? solutionPath, CancellationToken ct) { Commands.Add($"dotnet build --no-restore {solutionPath ?? "<auto>"} in {repoPath}"); return Task.FromResult(Pop()); }
    public Task<ShellResult> DotnetTestAsync(string repoPath, string? solutionPath, CancellationToken ct) { Commands.Add($"dotnet test --no-build {solutionPath ?? "<auto>"} in {repoPath}"); return Task.FromResult(Pop()); }
}
