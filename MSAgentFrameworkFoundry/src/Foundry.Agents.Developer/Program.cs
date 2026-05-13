using Foundry.Agents.Developer.Mcp;
using Foundry.Agents.Developer.Orchestration;
using Foundry.Agents.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddProblemDetails();

// Orchestrator wired in Task 8; for now register a placeholder that throws.
builder.Services.AddSingleton<IAssignTaskOrchestrator, ThrowingOrchestrator>();
builder.Services.AddSingleton<AssignDotnetTaskTool>();

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapMcp("/mcp");
app.MapGet("/", () => Results.Text("Foundry.Agents.Developer"));
app.Run();

public partial class Program;

internal sealed class ThrowingOrchestrator : IAssignTaskOrchestrator
{
    public Task<Foundry.Agents.Contracts.Mcp.AssignTaskResult> HandleAsync(
        Foundry.Agents.Contracts.Mcp.AssignTaskRequest request, CancellationToken ct)
        => throw new NotImplementedException("Wired in Task 8");
}
