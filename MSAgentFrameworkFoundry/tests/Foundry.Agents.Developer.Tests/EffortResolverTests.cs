using FluentAssertions;
using Foundry.Agents.Contracts;
using Foundry.Agents.Contracts.Chat;
using Xunit;

namespace Foundry.Agents.Developer.Tests;

public sealed class EffortResolverTests
{
    private static EffortResolver MakeResolver(EffortLevel configDefault = EffortLevel.Xhigh) =>
        new(configDefault);

    [Fact]
    public void Resolves_request_override_over_thread_and_config()
    {
        MakeResolver().Resolve(requestEffort: EffortLevel.Low, threadEffort: EffortLevel.High)
            .Should().Be(EffortLevel.Low);
    }

    [Fact]
    public void Resolves_thread_value_when_no_request_override()
    {
        MakeResolver().Resolve(requestEffort: null, threadEffort: EffortLevel.Medium)
            .Should().Be(EffortLevel.Medium);
    }

    [Fact]
    public void Resolves_config_default_when_neither_request_nor_thread_set()
    {
        MakeResolver(EffortLevel.Xhigh).Resolve(requestEffort: null, threadEffort: null)
            .Should().Be(EffortLevel.Xhigh);
    }
}
