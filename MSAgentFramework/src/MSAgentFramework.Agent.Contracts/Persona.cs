using System.Reflection;

namespace MSAgentFramework.Agent.Contracts;

public sealed record Persona(string Name, string Instructions)
{
    private static readonly Assembly DefiningAssembly = typeof(Persona).Assembly;

    public static Persona Load(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var resourceName = $"{DefiningAssembly.GetName().Name}.{name}.md";
        using var stream = DefiningAssembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Persona '{name}' not found. Looked for embedded resource '{resourceName}'. " +
                $"Add '{name}.md' to {DefiningAssembly.GetName().Name} as <EmbeddedResource>.");
        using var reader = new StreamReader(stream);
        return new Persona(name, reader.ReadToEnd().TrimEnd());
    }
}
