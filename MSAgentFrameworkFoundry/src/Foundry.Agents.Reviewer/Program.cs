using Azure.Identity;
using Foundry.Agents.Contracts.Chat;
using Foundry.Agents.Memory;
using Foundry.Agents.Reviewer;
using Foundry.Agents.Reviewer.GitHubMcp;
using Foundry.Agents.Reviewer.Mcp;
using Foundry.Agents.Reviewer.Orchestration;
using Foundry.Agents.ServiceDefaults;
using Microsoft.Azure.Cosmos;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddProblemDetails();

builder.Services.AddSingleton(sp =>
{
    var endpoint = builder.Configuration["Cosmos:Endpoint"]
        ?? throw new InvalidOperationException("Cosmos:Endpoint missing");
    return new CosmosClient(endpoint, new DefaultAzureCredential());
});
builder.Services.AddSingleton<ICosmosThreadStore>(sp =>
{
    var c = sp.GetRequiredService<CosmosClient>();
    var container = c.GetContainer(
        builder.Configuration["Cosmos:DatabaseName"]!,
        builder.Configuration["Cosmos:ContainerName"]!);
    return new CosmosThreadStore(container, TimeProvider.System);
});
builder.Services.AddSingleton(new FoundryChatOptions
{
    Endpoint = builder.Configuration["FoundryChat:Endpoint"]!,
    DeploymentName = builder.Configuration["FoundryChat:DeploymentName"]!,
});
builder.Services.AddSingleton<IChatClientFactory>(sp =>
    new ReviewerChatClientFactory(sp.GetRequiredService<FoundryChatOptions>()));
builder.Services.AddSingleton(new EffortResolver(
    Enum.Parse<Foundry.Agents.Contracts.EffortLevel>(
        builder.Configuration["Reviewer:DefaultEffort"] ?? "High")));
builder.Services.AddSingleton<IReviewerGitHubMcpClient>(_ =>
    throw new NotImplementedException("Wire IReviewerGitHubMcpClient at runtime via config"));
builder.Services.AddSingleton<IReviewerOrchestrator, ReviewerOrchestrator>();
builder.Services.AddSingleton<ReviewPullRequestTool>();

builder.Services.AddMcpServer().WithHttpTransport().WithToolsFromAssembly();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapMcp("/mcp");
app.MapGet("/", () => Results.Text("Foundry.Agents.Reviewer"));
app.Run();

public partial class Program;
