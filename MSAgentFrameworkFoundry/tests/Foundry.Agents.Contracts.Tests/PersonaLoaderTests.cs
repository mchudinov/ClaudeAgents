using System.Reflection;
using FluentAssertions;
using Foundry.Agents.Contracts.Personas;
using Xunit;

namespace Foundry.Agents.Contracts.Tests;

public sealed class PersonaLoaderTests
{
    [Fact]
    public async Task LoadAsync_returns_embedded_persona_markdown_verbatim()
    {
        var content = await PersonaLoader.LoadAsync(Assembly.GetExecutingAssembly());

        content.Should().Contain("# Test Persona");
        content.Should().Contain("PersonaLoaderTests");
    }

    [Fact]
    public async Task LoadAsync_throws_when_resource_missing()
    {
        // Pass an assembly that has no persona.md embedded
        var assemblyWithoutResource = typeof(EffortBudget).Assembly;

        var act = () => PersonaLoader.LoadAsync(assemblyWithoutResource);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*persona.md*not found*");
    }
}
