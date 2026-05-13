using FluentAssertions;
using Foundry.Agents.Contracts.Mcp;
using Foundry.Agents.Reviewer.Verdict;
using Xunit;

namespace Foundry.Agents.Reviewer.Tests;

public sealed class VerdictMapperTests
{
    [Theory]
    [InlineData("APPROVE",         ReviewVerdict.Approved)]
    [InlineData("approve",         ReviewVerdict.Approved)]
    [InlineData("REQUEST_CHANGES", ReviewVerdict.ChangesRequested)]
    [InlineData("REJECT",          ReviewVerdict.RejectedBlocking)]
    [InlineData("blocking",        ReviewVerdict.RejectedBlocking)]
    public void Maps_recognized_strings_to_enum(string model, ReviewVerdict expected)
    {
        VerdictMapper.FromModelOutput(model).Should().Be(expected);
    }

    [Fact]
    public void Throws_on_unknown_verdict()
    {
        var act = () => VerdictMapper.FromModelOutput("MAYBE_LATER");
        act.Should().Throw<InvalidOperationException>().WithMessage("*unrecognized verdict*");
    }
}
