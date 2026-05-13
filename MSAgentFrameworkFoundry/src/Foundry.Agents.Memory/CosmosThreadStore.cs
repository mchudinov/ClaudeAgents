using Microsoft.Azure.Cosmos;

namespace Foundry.Agents.Memory;

public sealed class CosmosThreadStore : ICosmosThreadStore
{
    private readonly Container _container;
    private readonly TimeProvider _clock;
    private readonly int _defaultTtlSeconds;

    public CosmosThreadStore(Container container, TimeProvider clock, int defaultTtlSeconds = 604_800)
    {
        _container = container ?? throw new ArgumentNullException(nameof(container));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _defaultTtlSeconds = defaultTtlSeconds;
    }

    public async Task<AgentThread> LoadOrCreateAsync(
        string threadId,
        AgentRole role,
        string personaMarkdown,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(personaMarkdown);

        var pk = role.ToPartitionKey();
        try
        {
            var response = await _container.ReadItemAsync<AgentThread>(threadId, new PartitionKey(pk), cancellationToken: ct);
            response.Resource.ETag = response.ETag;
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            var now = _clock.GetUtcNow();
            var thread = new AgentThread
            {
                Id = threadId,
                AgentRole = pk,
                CreatedUtc = now,
                UpdatedUtc = now,
                Ttl = _defaultTtlSeconds,
                Messages = { new ThreadMessage("system", personaMarkdown) },
            };
            var created = await _container.CreateItemAsync(thread, new PartitionKey(pk), cancellationToken: ct);
            created.Resource.ETag = created.ETag;
            return created.Resource;
        }
    }

    public async Task SaveAsync(AgentThread thread, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(thread);

        if (string.IsNullOrEmpty(thread.ETag))
            throw new InvalidOperationException(
                "AgentThread.ETag must be non-empty before SaveAsync. " +
                "If creating a new thread, use LoadOrCreateAsync instead of SaveAsync.");

        thread.UpdatedUtc = _clock.GetUtcNow();
        thread.Ttl = _defaultTtlSeconds;  // refresh TTL on every write — Task 11 invariant
        var pk = new PartitionKey(thread.AgentRole);

        var options = new ItemRequestOptions { IfMatchEtag = thread.ETag };
        var response = await _container.UpsertItemAsync(thread, pk, options, ct);
        thread.ETag = response.ETag;
    }

    public async Task CompactAsync(string threadId, AgentRole role, string summary, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);

        var pk = role.ToPartitionKey();
        var read = await _container.ReadItemAsync<AgentThread>(threadId, new PartitionKey(pk), cancellationToken: ct);
        var thread = read.Resource;
        thread.ETag = read.ETag;

        var persona = thread.Messages.FirstOrDefault()
            ?? throw new InvalidOperationException($"Thread '{threadId}' has no persona message — cannot compact.");
        if (persona.Role != "system")
            throw new InvalidOperationException(
                $"Thread '{threadId}' first message is role '{persona.Role}'; expected 'system' (persona).");

        thread.Messages = new List<ThreadMessage>
        {
            persona,
            new("system", $"Prior context summary: {summary}"),
            new("assistant", summary),
        };
        thread.Summary = summary;

        await SaveAsync(thread, ct);
    }
}
