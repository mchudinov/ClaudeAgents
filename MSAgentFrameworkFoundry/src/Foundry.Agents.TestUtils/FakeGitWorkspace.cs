namespace Foundry.Agents.TestUtils;

/// <summary>
/// Temporary placeholder for Plan 2's IGitWorkspace. Plan 2 T5 will move the interface to
/// Foundry.Agents.Developer.GitWorkspace and re-target this fake.
/// </summary>
public sealed class FakeGitWorkspace
{
    public List<string> Commands { get; } = new();
    public void Record(string command) => Commands.Add(command);
}
