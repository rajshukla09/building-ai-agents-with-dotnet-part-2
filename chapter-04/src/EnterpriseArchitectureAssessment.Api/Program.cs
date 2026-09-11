using EnterpriseArchitectureAssessment.Api.Contracts;
using EnterpriseArchitectureAssessment.Api.DependencyInjection;
using EnterpriseArchitectureAssessment.Api.Orchestration;
using EnterpriseArchitectureAssessment.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using EnterpriseArchitectureAssessment.Api.Mcp.Servers;

var mcpServerIndex = Array.IndexOf(args, "--mcp-server");
if (mcpServerIndex >= 0)
{
    if (mcpServerIndex + 1 >= args.Length) throw new ArgumentException("An MCP server name must follow --mcp-server.");
    var mcpHost = Host.CreateApplicationBuilder(args);
    mcpHost.Logging.ClearProviders();
    var mcp = mcpHost.Services.AddMcpServer().WithStdioServerTransport();
    switch (args[mcpServerIndex + 1])
    {
        case "architecture": mcp.WithTools<ArchitectureMcpServer>(); break;
        case "security": mcp.WithTools<SecurityMcpServer>(); break;
        case "cost": mcp.WithTools<CostMcpServer>(); break;
        case "operations": mcp.WithTools<OperationsMcpServer>(); break;
        case "migration": mcp.WithTools<MigrationMcpServer>(); break;
        case "research": mcp.WithTools<ResearchMcpServer>(); break;
        default: throw new ArgumentException($"Unknown MCP server '{args[mcpServerIndex + 1]}'.");
    }
    await mcpHost.Build().RunAsync();
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEnterpriseAssessment(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(x => x.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "App_Data"));
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AssessmentDbContext>().Database.Migrate();
}

app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();

app.MapPost("/api/assessments", async (StartAssessmentRequest request, IAssessmentService service,
                                       CancellationToken ct) => Results.Ok(await service.StartAsync(request, ct)))
    .WithOpenApi();
app.MapGet("/api/assessments", (IAssessmentRunStore store) => store.List()).WithOpenApi();
app.MapGet("/api/assessments/{id:guid}",
           (Guid id, IAssessmentRunStore store) =>
               store.Get(id) is { } run ? Results.Ok(run) : Results.NotFound())
    .WithOpenApi();
app.MapGet("/api/assessments/{id:guid}/events",
           (Guid id, IAssessmentRunStore store) =>
               store.Get(id) is { } run ? Results.Ok(run.Events) : Results.NotFound())
    .WithOpenApi();
app.MapGet("/api/assessments/compare", (Guid left, Guid right, IAssessmentRunStore store) =>
{
    var leftRun = store.Get(left);
    var rightRun = store.Get(right);
    return leftRun is null || rightRun is null
        ? Results.NotFound()
        : Results.Ok(AssessmentComparisonService.Compare(leftRun, rightRun));
}).WithOpenApi();

app.Run();

public partial class Program;
