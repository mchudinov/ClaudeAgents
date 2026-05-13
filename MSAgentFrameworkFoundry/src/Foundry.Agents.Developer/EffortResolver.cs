using Foundry.Agents.Contracts;

namespace Foundry.Agents.Developer;

public sealed class EffortResolver
{
    private readonly EffortLevel _configDefault;
    public EffortResolver(EffortLevel configDefault) => _configDefault = configDefault;

    public EffortLevel Resolve(EffortLevel? requestEffort, EffortLevel? threadEffort)
        => requestEffort ?? threadEffort ?? _configDefault;
}
