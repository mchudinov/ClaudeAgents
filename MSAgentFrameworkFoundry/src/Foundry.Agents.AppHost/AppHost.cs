using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var cosmos = builder.AddAzureCosmosDB("threads").RunAsEmulator();
var agentDb = cosmos.AddCosmosDatabase("agentdb");
agentDb.AddContainer("agent-threads", "/agentRole");

var foundryEndpoint = builder.AddParameter("anthropic-foundry-endpoint", secret: false);
var githubMcp = builder.AddParameter("github-mcp-endpoint", secret: false);
var githubPat = builder.AddParameter("github-pat", secret: true);

var reviewer = builder.AddProject<Projects.Foundry_Agents_Reviewer>("reviewer")
    .WithReference(cosmos)
    .WithEnvironment("FoundryChat__Endpoint", foundryEndpoint)
    .WithEnvironment("Reviewer__GitHubMcpEndpoint", githubMcp);

builder.AddProject<Projects.Foundry_Agents_Developer>("developer")
    .WithReference(cosmos)
    .WithEnvironment("FoundryChat__Endpoint", foundryEndpoint)
    .WithEnvironment("Developer__GitHubMcpEndpoint", githubMcp)
    .WithEnvironment("GITHUB_PAT", githubPat)
    .WithReference(reviewer)
    .WithEnvironment("Developer__ReviewerMcpEndpoint", reviewer.GetEndpoint("https"));

builder.Build().Run();
