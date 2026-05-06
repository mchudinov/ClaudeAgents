using CommunityToolkit.Aspire.Hosting.Dapr;

var builder = DistributedApplication.CreateBuilder(args);

var anthropicApiKey = builder.Configuration["ANTHROPIC_API_KEY"]
    ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
    ?? throw new InvalidOperationException("ANTHROPIC_API_KEY is not set. Add it to user-secrets, environment, or appsettings before running the AppHost.");

// daprd inherits its environment from the AppHost process; the envvar-secretstore
// component reads ANTHROPIC_API_KEY from there, so it must be present here too.
Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", anthropicApiKey);

var redis = builder.AddRedis("redis").WithDataVolume();

var agent = builder.AddProject<Projects.MSAgentFramework_Agent>("agent")
    .WithEnvironment("ANTHROPIC_API_KEY", anthropicApiKey)
    .WithReference(redis)
    .WithDaprSidecar(new DaprSidecarOptions
    {
        AppId = "dotnet-csharp-agent",
        AppPort = 8085,
        ResourcesPaths = ["../../../deploy/dapr/components"]
    });

builder.Build().Run();
