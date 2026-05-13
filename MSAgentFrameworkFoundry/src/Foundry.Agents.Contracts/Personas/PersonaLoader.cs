using System.Reflection;

namespace Foundry.Agents.Contracts.Personas;

public static class PersonaLoader
{
    private const string ResourceName = "persona.md";

    public static async Task<string> LoadAsync(Assembly assembly, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var fullName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(ResourceName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' not found in assembly '{assembly.GetName().Name}'. " +
                $"Check the project's <EmbeddedResource Include=\"...\" LogicalName=\"persona.md\" /> entry.");

        await using var stream = assembly.GetManifestResourceStream(fullName)
            ?? throw new InvalidOperationException($"Could not open resource stream for '{fullName}'.");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct);
    }
}
