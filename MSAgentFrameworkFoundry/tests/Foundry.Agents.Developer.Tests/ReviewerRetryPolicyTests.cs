using FluentAssertions;
using Foundry.Agents.Developer.Reviewer;
using Xunit;

namespace Foundry.Agents.Developer.Tests;

public sealed class ReviewerRetryPolicyTests
{
    [Fact]
    public async Task Retries_twice_after_first_failure_for_a_total_of_three_attempts()
    {
        var pipeline = ReviewerRetryPolicy.Build(
            backoffMultiplier: TimeSpan.FromMilliseconds(1),
            maxBackoff: TimeSpan.FromMilliseconds(10));

        var attempts = 0;
        var act = async () => await pipeline.ExecuteAsync(_ =>
        {
            attempts++;
            throw new HttpRequestException("nope");
#pragma warning disable CS0162 // Unreachable code detected
            return ValueTask.CompletedTask;
#pragma warning restore CS0162
        }, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
        attempts.Should().Be(3);
    }

    [Fact]
    public async Task Succeeds_on_third_attempt_returns_value()
    {
        var pipeline = ReviewerRetryPolicy.Build(
            backoffMultiplier: TimeSpan.FromMilliseconds(1),
            maxBackoff: TimeSpan.FromMilliseconds(10));

        var attempts = 0;
        var result = await pipeline.ExecuteAsync(async _ =>
        {
            attempts++;
            if (attempts < 3) throw new HttpRequestException("transient");
            await Task.Yield();
            return 42;
        }, CancellationToken.None);

        result.Should().Be(42);
        attempts.Should().Be(3);
    }
}
