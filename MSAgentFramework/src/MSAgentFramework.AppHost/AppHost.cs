using CommunityToolkit.Aspire.Hosting.Dapr;

var builder = DistributedApplication.CreateBuilder(args);

var redis = builder.AddRedis("redis").WithDataVolume();

var agent = builder.AddProject<Projects.MSAgentFramework_Agent>("agent")
    .WithEnvironment("ANTHROPIC_API_KEY", builder.Configuration["ANTHROPIC_API_KEY"]
        ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"))
    .WithReference(redis)
    .WithDaprSidecar(new DaprSidecarOptions
    {
        AppId = "dotnet-csharp-agent",
        AppPort = 8085,
        ResourcesPaths = ["../../../deploy/dapr/components"]
    });

builder.Build().Run();
