using FluentAssertions;
using Foundry.Agents.Contracts;
using Foundry.Agents.Developer.Compactor;
using Foundry.Agents.Memory;
using Foundry.Agents.TestUtils;
using Microsoft.Extensions.AI;
using NSubstitute;
using Xunit;

namespace Foundry.Agents.Developer.Tests;

public sealed class ThreadCompactorTests
{
    [Fact]
    public async Task CompactAsync_calls_model_with_summary_prompt_and_persists_summary()
    {
        var chat = new FakeChatClient();
        chat.QueueResponse("Implemented retry policy. PR https://x/142 approved.");
        var store = Substitute.For<ICosmosThreadStore>();
        var thread = new AgentThread
        {
            Id = "tid", AgentRole = "developer",
            CreatedUtc = DateTimeOffset.UtcNow,
            Messages = { new ThreadMessage("system", "# Persona"), new ThreadMessage("user", "fix bug"), new ThreadMessage("assistant", "did") },
            ETag = "e",
        };

        var compactor = new ThreadCompactor(store, new TestChatClientFactory(chat));
        var summary = await compactor.CompactAsync(thread, EffortLevel.Low, default);

        summary.Should().Contain("retry policy");
        await store.Received(1).CompactAsync("tid", AgentRole.Developer, summary, Arg.Any<CancellationToken>());
        chat.Calls.Should().HaveCount(1);
        chat.Calls[0].Messages.Last().Text.Should().Contain("Summarize this conversation in ≤500 tokens");
    }
}
