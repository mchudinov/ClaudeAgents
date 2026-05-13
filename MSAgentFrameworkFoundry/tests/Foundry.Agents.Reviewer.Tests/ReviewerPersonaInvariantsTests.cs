using FluentAssertions;
using Foundry.Agents.Contracts.Personas;
using Xunit;

namespace Foundry.Agents.Reviewer.Tests;

public sealed class ReviewerPersonaInvariantsTests
{
    [Fact]
    public async Task Persona_is_non_empty()
    {
        var p = await PersonaLoader.LoadAsync(typeof(Program).Assembly);
        p.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Persona_says_approve_only_via_pull_request_review_write()
    {
        var p = await PersonaLoader.LoadAsync(typeof(Program).Assembly);
        p.Should().Contain("pull_request_review_write");
    }

    [Fact]
    public async Task Persona_forbids_calling_any_merge_tool()
    {
        var p = await PersonaLoader.LoadAsync(typeof(Program).Assembly);
        p.Should().MatchRegex("(?i)never (invoke|call) any merge");
    }

    [Fact]
    public async Task Persona_forbids_proposing_patches_and_lists_three_verdicts()
    {
        var p = await PersonaLoader.LoadAsync(typeof(Program).Assembly);
        p.Should().MatchRegex("(?i)do not propose patches");
        p.Should().Contain("Approved").And.Contain("ChangesRequested").And.Contain("RejectedBlocking");
    }
}
