using Azure.Identity;
using Foundry.Agents.Contracts.Chat;
using Microsoft.Extensions.AI;

namespace Foundry.Agents.Reviewer;

internal sealed class ReviewerChatClientFactory : IChatClientFactory
{
    private readonly FoundryChatOptions _options;
    public ReviewerChatClientFactory(FoundryChatOptions options) => _options = options;

    public IChatClient Create()
    {
        var inference = new Azure.AI.Inference.ChatCompletionsClient(
            new Uri(_options.Endpoint),
            new DefaultAzureCredential());
        return inference.AsIChatClient(_options.DeploymentName);
    }
}
