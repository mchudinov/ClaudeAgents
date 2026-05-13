namespace Foundry.Agents.Contracts;

public readonly record struct EffortBudget(int ThinkingBudgetTokens, int MaxOutputTokens)
{
    public static EffortBudget For(EffortLevel level) => level switch
    {
        EffortLevel.Low    => new EffortBudget(1_024,  4_096),
        EffortLevel.Medium => new EffortBudget(4_096,  8_192),
        EffortLevel.High   => new EffortBudget(16_384, 16_384),
        EffortLevel.Xhigh  => new EffortBudget(32_768, 32_768),
        EffortLevel.Max    => new EffortBudget(64_000, 64_000),
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown effort level"),
    };
}
