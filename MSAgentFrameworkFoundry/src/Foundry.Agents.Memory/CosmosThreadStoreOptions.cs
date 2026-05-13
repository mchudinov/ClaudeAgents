namespace Foundry.Agents.Memory;

public sealed class CosmosThreadStoreOptions
{
    public required string DatabaseName  { get; init; } = "agentdb";
    public required string ContainerName { get; init; } = "agent-threads";
    /// <summary>Default time-to-live for a thread document, in seconds. Refreshed on every write.</summary>
    public int DefaultTtlSeconds { get; init; } = 604_800;
}
