using CommunityToolkit.Aspire.Hosting.Dapr;

var builder = DistributedApplication.CreateBuilder(args);

var anthropicApiKey = builder.Configuration["ANTHROPIC_API_KEY"]
    ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
    ?? throw new InvalidOperationException("ANTHROPIC_API_KEY is not set. Add it to user-secrets, environment, or appsettings before running the AppHost.");

// daprd inherits its environment from the AppHost process; the envvar-secretstore
// component reads ANTHROPIC_API_KEY from there, so it must be present here too.
Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", anthropicApiKey);

var agent = builder.AddProject<Projects.MSAgentFramework_Agent>("agent")
    .WithEnvironment("ANTHROPIC_API_KEY", anthropicApiKey)
    .WithDaprSidecar(new DaprSidecarOptions
    {
        AppId = "dotnet-csharp-agent",
        AppPort = 8085,
        ResourcesPaths = ["../../../deploy/dapr/components"]
    });

builder.AddContainer("redisinsight", "redis/redisinsight")
    .WithImageTag("latest")
    .WithHttpEndpoint(port: 5540, targetPort: 5540, name: "http")
    .WithEnvironment("RI_REDIS_HOST", "host.docker.internal")
    .WithEnvironment("RI_REDIS_PORT", "6379")
    .WithEnvironment("RI_REDIS_USERNAME", "")
    .WithEnvironment("RI_REDIS_PASSWORD", "")
    .WithEnvironment("RI_REDIS_ALIAS", "Local Redis")
    .WithEnvironment("RI_ACCEPT_TERMS_AND_CONDITIONS", "true");

builder.Build().Run();
