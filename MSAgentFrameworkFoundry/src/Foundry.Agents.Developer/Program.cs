using Foundry.Agents.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
// MCP server, IChatClient, ICosmosThreadStore, IGitWorkspace, etc. wired in later tasks.

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapGet("/", () => Results.Text("Foundry.Agents.Developer"));
app.Run();

public partial class Program;
