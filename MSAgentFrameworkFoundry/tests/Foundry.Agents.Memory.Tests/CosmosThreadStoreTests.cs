using FluentAssertions;
using Foundry.Agents.Memory;
using Xunit;

namespace Foundry.Agents.Memory.Tests;

public sealed class CosmosThreadStoreTests : IClassFixture<CosmosEmulatorFixture>
{
    private readonly CosmosEmulatorFixture _fixture;
    private readonly CosmosThreadStore _store;

    public CosmosThreadStoreTests(CosmosEmulatorFixture fixture)
    {
        _fixture = fixture;
        _store = new CosmosThreadStore(_fixture.ThreadsContainer, TimeProvider.System);
    }

    // Task 7: LoadOrCreateAsync new-thread path
    [Fact]
    public async Task LoadOrCreateAsync_creates_new_thread_with_persona_as_first_system_message()
    {
        var threadId = "01J6Z8K000000000000000001";
        var persona = "# Test Persona\n\nThe rules.";

        var thread = await _store.LoadOrCreateAsync(threadId, AgentRole.Developer, persona, CancellationToken.None);

        thread.Id.Should().Be(threadId);
        thread.AgentRole.Should().Be("developer");
        thread.Messages.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ThreadMessage("system", persona));
        thread.Ttl.Should().Be(604_800);
        thread.ETag.Should().NotBeNullOrEmpty();
    }

    // Task 8: LoadOrCreateAsync existing-thread round-trip + SaveAsync
    [Fact]
    public async Task LoadOrCreateAsync_returns_existing_thread_with_all_fields_round_tripped()
    {
        var threadId = "01J6Z8K000000000000000002";
        var persona  = "# Persona";
        var original = await _store.LoadOrCreateAsync(threadId, AgentRole.Developer, persona, CancellationToken.None);
        original.GithubRepo = "octocat/hello-world";
        original.PrNumber = 142;
        original.ReviewRound = 2;
        original.Effort = Foundry.Agents.Contracts.EffortLevel.Xhigh;
        original.LinkedReviewThreadId = "01J6Z8K000000000000000003";
        original.Messages.Add(new ThreadMessage("user", "add retry to OrderService"));
        await _store.SaveAsync(original, CancellationToken.None);

        var reloaded = await _store.LoadOrCreateAsync(threadId, AgentRole.Developer, persona, CancellationToken.None);

        reloaded.Should().BeEquivalentTo(original, o => o.Excluding(x => x.ETag).Excluding(x => x.UpdatedUtc));
        reloaded.ETag.Should().NotBe(original.ETag);  // SaveAsync produced a new etag
    }

    // Task 9: SaveAsync ETag optimistic concurrency (412 path)
    [Fact]
    public async Task SaveAsync_throws_when_etag_does_not_match()
    {
        var threadId = "01J6Z8K000000000000000004";
        var persona  = "# Persona";
        var a = await _store.LoadOrCreateAsync(threadId, AgentRole.Developer, persona, CancellationToken.None);
        var b = await _store.LoadOrCreateAsync(threadId, AgentRole.Developer, persona, CancellationToken.None);

        a.Messages.Add(new ThreadMessage("user", "first writer"));
        await _store.SaveAsync(a, CancellationToken.None);                  // succeeds, etag advances

        b.Messages.Add(new ThreadMessage("user", "second writer"));
        var act = () => _store.SaveAsync(b, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<Microsoft.Azure.Cosmos.CosmosException>();
        ex.Which.StatusCode.Should().Be(System.Net.HttpStatusCode.PreconditionFailed);
    }

    // Task 10: CompactAsync replaces history with persona + summary
    [Fact]
    public async Task CompactAsync_replaces_history_with_persona_plus_summary_messages()
    {
        var threadId = "01J6Z8K000000000000000005";
        var persona  = "# Developer Persona\nrules.";
        var summary  = "Implemented retry policy in OrderService. PR #142 approved.";

        var t = await _store.LoadOrCreateAsync(threadId, AgentRole.Developer, persona, CancellationToken.None);
        t.Messages.Add(new ThreadMessage("user", "add retry"));
        t.Messages.Add(new ThreadMessage("assistant", "..."));
        t.Messages.Add(new ThreadMessage("user", "looks good?"));
        t.Messages.Add(new ThreadMessage("assistant", "..."));
        await _store.SaveAsync(t, CancellationToken.None);

        await _store.CompactAsync(threadId, AgentRole.Developer, summary, CancellationToken.None);

        var reloaded = await _store.LoadOrCreateAsync(threadId, AgentRole.Developer, persona, CancellationToken.None);
        reloaded.Messages.Should().HaveCount(3);
        reloaded.Messages[0].Should().BeEquivalentTo(new ThreadMessage("system", persona));
        reloaded.Messages[1].Should().BeEquivalentTo(new ThreadMessage("system", $"Prior context summary: {summary}"));
        reloaded.Messages[2].Should().BeEquivalentTo(new ThreadMessage("assistant", summary));
        reloaded.Summary.Should().Be(summary);
    }

    // Task 11: TTL refresh on every write
    [Fact]
    public async Task SaveAsync_refreshes_ttl_on_each_write()
    {
        var threadId = "01J6Z8K000000000000000006";
        var t = await _store.LoadOrCreateAsync(threadId, AgentRole.Developer, "# p", CancellationToken.None);
        t.Ttl = 60;  // simulate prior decay
        await _store.SaveAsync(t, CancellationToken.None);

        var reloaded = await _store.LoadOrCreateAsync(threadId, AgentRole.Developer, "# p", CancellationToken.None);

        reloaded.Ttl.Should().Be(604_800);
    }
}
