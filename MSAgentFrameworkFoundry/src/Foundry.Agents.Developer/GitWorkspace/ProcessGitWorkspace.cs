using System.Diagnostics;

namespace Foundry.Agents.Developer.GitWorkspace;

public sealed class ProcessGitWorkspace : IGitWorkspace
{
    private readonly string _githubPat;
    public ProcessGitWorkspace(string githubPat) => _githubPat = githubPat;

    public Task<ShellResult> CloneAsync(CloneRequest request, CancellationToken ct)
    {
        var url = request.RepoUrl.Replace("https://github.com/", $"https://x-access-token:{_githubPat}@github.com/", StringComparison.Ordinal);
        var args = request.Branch is null
            ? $"clone \"{url}\" \"{request.DestinationPath}\""
            : $"clone -b \"{request.Branch}\" \"{url}\" \"{request.DestinationPath}\"";
        return RunAsync("git", args, workingDir: null, ct);
    }

    public Task<ShellResult> CheckoutNewBranchAsync(string repoPath, string branch, CancellationToken ct)
        => RunAsync("git", $"checkout -b \"{branch}\"", repoPath, ct);

    public Task<ShellResult> CommitAllAsync(string repoPath, string message, CancellationToken ct)
        => RunChainAsync(repoPath, ct,
            ("git", "add ."),
            ("git", $"-c user.email=dev@foundry.agent -c user.name=\"Foundry Developer Agent\" commit -m \"{message.Replace("\"", "\\\"")}\""));

    public Task<ShellResult> PushAsync(string repoPath, string branch, CancellationToken ct)
        => RunAsync("git", $"push -u origin \"{branch}\"", repoPath, ct);

    public Task<ShellResult> DotnetRestoreAsync(string repoPath, string? solutionPath, CancellationToken ct)
        => RunAsync("dotnet", $"restore --nologo{TargetArg(solutionPath)}", repoPath, ct);

    public Task<ShellResult> DotnetBuildAsync(string repoPath, string? solutionPath, CancellationToken ct)
        => RunAsync("dotnet", $"build --nologo --no-restore{TargetArg(solutionPath)}", repoPath, ct);

    public Task<ShellResult> DotnetTestAsync(string repoPath, string? solutionPath, CancellationToken ct)
        => RunAsync("dotnet", $"test --nologo --no-build{TargetArg(solutionPath)}", repoPath, ct);

    private static string TargetArg(string? solutionPath) =>
        string.IsNullOrWhiteSpace(solutionPath) ? "" : $" \"{solutionPath}\"";

    private static async Task<ShellResult> RunAsync(string file, string args, string? workingDir, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDir ?? Environment.CurrentDirectory,
        };
        using var p = Process.Start(psi)!;
        var stdoutTask = p.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return new ShellResult(p.ExitCode, await stdoutTask, await stderrTask);
    }

    private static async Task<ShellResult> RunChainAsync(string workingDir, CancellationToken ct, params (string file, string args)[] steps)
    {
        ShellResult? last = null;
        foreach (var (f, a) in steps)
        {
            last = await RunAsync(f, a, workingDir, ct);
            if (last.ExitCode != 0) return last;
        }
        return last!;
    }
}
