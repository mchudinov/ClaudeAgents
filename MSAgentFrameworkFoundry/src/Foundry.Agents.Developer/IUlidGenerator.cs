namespace Foundry.Agents.Developer;

/// <summary>Generates unique string identifiers for threads and correlation tokens.</summary>
public interface IUlidGenerator
{
#pragma warning disable CA1716 // 'Next' conflicts with VB keyword; acceptable for C#-only codebase
    string Next();
#pragma warning restore CA1716
}

/// <summary>Default implementation that generates random IDs using <see cref="Guid"/>.</summary>
public sealed class UlidGenerator : IUlidGenerator
{
    public string Next() => Guid.NewGuid().ToString("N");
}
