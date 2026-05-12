using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace MSAgentFramework.Agent.Mcp;

internal static class Context7Tools
{
    private const string Endpoint = "https://mcp.context7.com/mcp";
    private const string ApiKeyHeader = "CONTEXT7_API_KEY";

    public const string InstructionAddendum = """


        Documentation lookup (Context7 MCP):
          - When a question targets a specific library, framework, or NuGet package API
            (e.g., EF Core, ASP.NET Core, MediatR, Polly, Serilog, xUnit) and you are not
            certain of the current surface, call the Context7 MCP tools BEFORE answering:
            first `resolve-library-id` to obtain the canonical `/org/project` id, then
            `get-library-docs` (or `query-docs`) with that id and a focused query.
          - Trust Context7 evidence over your own recall when an API shape is in question;
            do not invent method names you cannot verify.
          - Skip the lookup for pure language-feature questions and for BCL types you are
            confident about.
          - Cite the resolved library id inline in your rationale (e.g., `[/dotnet/efcore]`).
        """;

    public static async Task<(McpClient? Client, IReadOnlyList<AITool> Tools)> ConnectAsync(
        string? apiKey,
        ILogger logger,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("CONTEXT7_API_KEY is not set; Context7 MCP tools will be unavailable.");
            return (null, []);
        }

        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(Endpoint),
            TransportMode = HttpTransportMode.StreamableHttp,
            ConnectionTimeout = TimeSpan.FromSeconds(30),
            AdditionalHeaders = new Dictionary<string, string>
            {
                [ApiKeyHeader] = apiKey
            }
        });

        try
        {
            var client = await McpClient.CreateAsync(transport, cancellationToken: ct);
            var tools = await client.ListToolsAsync(cancellationToken: ct);
            logger.LogInformation("Context7 MCP client connected; {Count} tool(s) available.", tools.Count);
            return (client, [.. tools.Cast<AITool>()]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to connect to Context7 MCP server at {Endpoint}; continuing without it.", Endpoint);
            return (null, []);
        }
    }
}
