using Foundry.Agents.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddProblemDetails();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapGet("/", () => Results.Text("Foundry.Agents.Reviewer"));
app.Run();

public partial class Program;
