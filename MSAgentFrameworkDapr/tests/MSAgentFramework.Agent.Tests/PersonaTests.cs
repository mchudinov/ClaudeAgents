using MSAgentFramework.Agent.Contracts;

namespace MSAgentFramework.Agent.Tests;

public sealed class PersonaTests
{
    private static readonly Persona DotnetCsharpExpert = Persona.Load("dotnet-csharp-expert");

    [Fact]
    public void DotnetCsharpExpert_IsAdvisoryOnly()
    {
        Assert.Contains("advisory", DotnetCsharpExpert.Instructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do NOT", DotnetCsharpExpert.Instructions);
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
        Assert.Contains(marker, DotnetCsharpExpert.Instructions);
    }

    [Theory]
    [InlineData("Docker")]
    [InlineData("Kubernetes")]
    [InlineData("port-binding")]
    public void DotnetCsharpExpert_DoesNotCoverOutOfScope(string marker)
    {
        var firstHit = DotnetCsharpExpert.Instructions.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (firstHit < 0) return;

        // Allowed only in the explicit out-of-scope section; require it to be near "Out of scope".
        var outOfScopeIndex = DotnetCsharpExpert.Instructions.IndexOf("Out of scope", StringComparison.OrdinalIgnoreCase);
        Assert.True(outOfScopeIndex >= 0 && firstHit > outOfScopeIndex,
            $"{marker} appears before the 'Out of scope' section, suggesting it is being covered as in-scope guidance.");
    }

    [Fact]
    public void Name_IsDotnetCsharpExpert()
    {
        Assert.Equal("dotnet-csharp-expert", DotnetCsharpExpert.Name);
    }

    [Fact]
    public void Load_UnknownPersona_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Persona.Load("does-not-exist"));
        Assert.Contains("does-not-exist", ex.Message);
    }
}
