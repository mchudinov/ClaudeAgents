using Azure.Core;
using Azure.Identity;
using Foundry.Agents.Contracts;
using Foundry.Agents.Contracts.Chat;
using Microsoft.Extensions.AI;

namespace Foundry.Agents.Developer;

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
