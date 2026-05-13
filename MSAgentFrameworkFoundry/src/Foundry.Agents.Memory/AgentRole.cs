using System.Text.Json.Serialization;

namespace Foundry.Agents.Memory;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AgentRole
{
    Developer,
    Reviewer,
}

public static class AgentRoleExtensions
{
    public static string ToPartitionKey(this AgentRole role) => role switch
    {
        AgentRole.Developer => "developer",
        AgentRole.Reviewer  => "reviewer",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };
}
