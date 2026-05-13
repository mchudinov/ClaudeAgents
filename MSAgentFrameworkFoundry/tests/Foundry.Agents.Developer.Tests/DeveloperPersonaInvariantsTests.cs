using FluentAssertions;
using Foundry.Agents.Contracts.Personas;
using Xunit;

namespace Foundry.Agents.Developer.Tests;

public sealed class DeveloperPersonaInvariantsTests
{
    [Fact]
    public async Task Persona_is_non_empty()
    {
        var persona = await PersonaLoader.LoadAsync(typeof(Program).Assembly);
        persona.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Persona_instructs_dotnet_build_and_dotnet_test_before_pushing()
    {
        var persona = await PersonaLoader.LoadAsync(typeof(Program).Assembly);
        persona.Should().Contain("dotnet build").And.Contain("dotnet test");
        persona.Should().MatchRegex("(?i)before pushing|before push|prior to push");
    }

    [Fact]
    public async Task Persona_forbids_pushing_to_default_branch()
    {
        var persona = await PersonaLoader.LoadAsync(typeof(Program).Assembly);
        persona.Should().MatchRegex("(?i)never push.*default branch");
    }

    [Fact]
    public async Task Persona_says_one_pr_per_task_and_never_merges()
    {
        var persona = await PersonaLoader.LoadAsync(typeof(Program).Assembly);
        persona.Should().MatchRegex("(?i)one PR per task");
        persona.Should().MatchRegex("(?i)never merge");
    }
}
