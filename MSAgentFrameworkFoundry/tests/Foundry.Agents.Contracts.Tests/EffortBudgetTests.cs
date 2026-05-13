using FluentAssertions;
using Xunit;

namespace Foundry.Agents.Contracts.Tests;

public sealed class EffortBudgetTests
{
    [Theory]
    [InlineData(EffortLevel.Low,    1024,  4096)]
    [InlineData(EffortLevel.Medium, 4096,  8192)]
    [InlineData(EffortLevel.High,   16384, 16384)]
    [InlineData(EffortLevel.Xhigh,  32768, 32768)]
    [InlineData(EffortLevel.Max,    64000, 64000)]
    public void For_maps_each_level_to_the_spec_table(EffortLevel level, int thinking, int output)
    {
        var budget = EffortBudget.For(level);

        budget.ThinkingBudgetTokens.Should().Be(thinking);
        budget.MaxOutputTokens.Should().Be(output);
    }

    [Fact]
    public void For_throws_on_undefined_enum_value()
    {
        var act = () => EffortBudget.For((EffortLevel)999);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
