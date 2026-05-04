using MSAgentFramework.Agent.Contracts;

namespace MSAgentFramework.Agent.Tests;

public sealed class PersonaTests
{
    [Fact]
    public void DotnetCsharpExpert_IsAdvisoryOnly()
    {
        Assert.Contains("advisory", Persona.DotnetCsharpExpert, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do NOT", Persona.DotnetCsharpExpert);
    }

    [Theory]
    [InlineData("records")]
    [InlineData("async/await")]
    [InlineData("LINQ")]
    [InlineData("EF Core")]
    [InlineData("xUnit")]
    [InlineData("ASP.NET Core")]
    public void DotnetCsharpExpert_CoversFocusArea(string marker)
    {
        Assert.Contains(marker, Persona.DotnetCsharpExpert);
    }

    [Theory]
    [InlineData("Docker")]
    [InlineData("Kubernetes")]
    [InlineData("port-binding")]
    public void DotnetCsharpExpert_DoesNotCoverOutOfScope(string marker)
    {
        var firstHit = Persona.DotnetCsharpExpert.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (firstHit < 0) return;

        // Allowed only in the explicit out-of-scope section; require it to be near "Out of scope".
        var outOfScopeIndex = Persona.DotnetCsharpExpert.IndexOf("Out of scope", StringComparison.OrdinalIgnoreCase);
        Assert.True(outOfScopeIndex >= 0 && firstHit > outOfScopeIndex,
            $"{marker} appears before the 'Out of scope' section, suggesting it is being covered as in-scope guidance.");
    }

    [Fact]
    public void Name_IsDotnetCsharpExpert()
    {
        Assert.Equal("dotnet-csharp-expert", Persona.Name);
    }
}
