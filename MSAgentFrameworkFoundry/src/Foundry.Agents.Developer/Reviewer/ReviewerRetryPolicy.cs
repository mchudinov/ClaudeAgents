using Polly;
using Polly.Retry;

namespace Foundry.Agents.Developer.Reviewer;

public static class ReviewerRetryPolicy
{
    public static ResiliencePipeline Build(
        TimeSpan? backoffMultiplier = null,
        TimeSpan? maxBackoff = null)
        => new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>(),
                MaxRetryAttempts = 2,
                BackoffType = DelayBackoffType.Exponential,
                Delay = backoffMultiplier ?? TimeSpan.FromSeconds(1),
                MaxDelay = maxBackoff ?? TimeSpan.FromSeconds(30),
                UseJitter = true,
            })
            .Build();
}
