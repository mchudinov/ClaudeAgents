using Azure.Core;
using Azure.Identity;
using Foundry.Agents.Contracts;
using Microsoft.Extensions.AI;

namespace Foundry.Agents.Developer;

public interface IChatClientFactory { IChatClient Create(); }

public sealed class FoundryChatOptions
{
    public required string Endpoint { get; init; }
    public required string DeploymentName { get; init; }
}

public sealed class ChatClientFactory : IChatClientFactory
{
    private readonly FoundryChatOptions _options;
    private readonly TokenCredential _credential;

    public ChatClientFactory(FoundryChatOptions options, TokenCredential? credential = null)
    {
        _options = options;
        _credential = credential ?? new DefaultAzureCredential();
    }

    public IChatClient Create()
    {
        var inference = new Azure.AI.Inference.ChatCompletionsClient(
            new Uri(_options.Endpoint),
            _credential);
        return inference.AsIChatClient(_options.DeploymentName);
    }

    public static ChatOptions ChatOptionsFor(EffortLevel level)
    {
        var budget = EffortBudget.For(level);
        return new ChatOptions
        {
            MaxOutputTokens = budget.MaxOutputTokens,
        };
    }
}
