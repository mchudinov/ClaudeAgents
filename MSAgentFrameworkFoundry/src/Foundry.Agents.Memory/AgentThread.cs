using System.Text.Json.Serialization;
using Foundry.Agents.Contracts;

namespace Foundry.Agents.Memory;

public sealed class AgentThread
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("agentRole")]
    public required string AgentRole { get; init; }

    [JsonPropertyName("createdUtc")]
    public required DateTimeOffset CreatedUtc { get; init; }

    [JsonPropertyName("updatedUtc")]
    public DateTimeOffset UpdatedUtc { get; set; }

    [JsonPropertyName("ttl")]
    public int Ttl { get; set; } = 604_800;

    [JsonPropertyName("linkedReviewThreadId")]
    public string? LinkedReviewThreadId { get; set; }

    [JsonPropertyName("githubRepo")]
    public string? GithubRepo { get; set; }

    [JsonPropertyName("prNumber")]
    public int? PrNumber { get; set; }

    [JsonPropertyName("reviewRound")]
    public int ReviewRound { get; set; }

    [JsonPropertyName("effort")]
    public EffortLevel? Effort { get; set; }

    [JsonPropertyName("messages")]
    public List<ThreadMessage> Messages { get; set; } = new();

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    /// <summary>Cosmos ETag carried in <see cref="_etag"/>; not part of the public contract, populated by the store.</summary>
    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}
