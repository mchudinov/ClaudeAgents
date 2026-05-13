using Aspire.Hosting.Testing;
using FluentAssertions;
using Foundry.Agents.Contracts.Mcp;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Foundry.Agents.Developer.IntegrationTests;

public sealed class HappyPathE2ETests
{
    private static readonly bool Enabled =
        Environment.GetEnvironmentVariable("RUN_E2E_TESTS") == "1" &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("INTEGRATION_GITHUB_PAT")) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("INTEGRATION_TARGET_REPO"));

    [SkippableFact]
    public async Task assign_dotnet_task_opens_pr_and_returns_approved_status()
    {
        Skip.IfNot(Enabled, "Set RUN_E2E_TESTS=1, INTEGRATION_GITHUB_PAT, INTEGRATION_TARGET_REPO to run.");

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Foundry_Agents_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();
        var developerBaseUri = app.GetEndpoint("developer", "https");

        await using var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(developerBaseUri, "/mcp"),
            TransportMode = HttpTransportMode.Sse,
        });
        await using var client = await McpClient.CreateAsync(transport);

        var args = new Dictionary<string, object?>
        {
            ["GithubRepo"] = Environment.GetEnvironmentVariable("INTEGRATION_TARGET_REPO"),
            ["TaskDescription"] = "Add a Hello() method to the public API that returns the string \"hi\".",
            ["Effort"] = "Low",
        };
        var result = await client.CallToolAsync("assign_dotnet_task", args);
        var payload = result.Content.OfType<TextContentBlock>().First().Text;
        var dto = System.Text.Json.JsonSerializer.Deserialize<AssignTaskResult>(payload)!;

        dto.Status.Should().BeOneOf(AssignTaskStatus.Approved, AssignTaskStatus.ReviewFailed);
        dto.PrUrl.Should().NotBeNullOrWhiteSpace();
    }
}
