using Foundry.Agents.Contracts;
using Microsoft.Extensions.AI;

namespace Foundry.Agents.Contracts.Chat;

public interface IChatClientFactory { IChatClient Create(); }

public sealed class FoundryChatOptions
{
    public required string Endpoint { get; init; }
    public required string DeploymentName { get; init; }
}

public sealed class EffortResolver
{
    private readonly EffortLevel _configDefault;
    public EffortResolver(EffortLevel configDefault) => _configDefault = configDefault;

    public EffortLevel Resolve(EffortLevel? requestEffort, EffortLevel? threadEffort)
        => requestEffort ?? threadEffort ?? _configDefault;
}
