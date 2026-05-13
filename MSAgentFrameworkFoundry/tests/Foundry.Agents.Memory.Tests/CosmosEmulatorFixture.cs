using Microsoft.Azure.Cosmos;
using Testcontainers.CosmosDb;
using Xunit;

namespace Foundry.Agents.Memory.Tests;

public sealed class CosmosEmulatorFixture : IAsyncLifetime
{
    private readonly CosmosDbContainer _container = new CosmosDbBuilder()
        .WithImage("mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview")
        .Build();

    public CosmosClient Client { get; private set; } = null!;
    public Container ThreadsContainer { get; private set; } = null!;
    public const string DatabaseName  = "agentdb";
    public const string ContainerName = "agent-threads";

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Client = new CosmosClient(_container.GetConnectionString(), new CosmosClientOptions
        {
            ConnectionMode = ConnectionMode.Gateway,
            // The emulator self-signed cert isn't trusted by the test runner.
            HttpClientFactory = () => new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            }),
        });

        var db = await Client.CreateDatabaseIfNotExistsAsync(DatabaseName);
        ThreadsContainer = (await db.Database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(ContainerName, "/agentRole")
            {
                DefaultTimeToLive = 604_800,
            })).Container;
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        await _container.DisposeAsync();
    }
}
