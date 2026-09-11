using EnterpriseIncidentInvestigator.Api.Agents;
using EnterpriseIncidentInvestigator.Api.Configuration;
using EnterpriseIncidentInvestigator.Api.Mcp;
using EnterpriseIncidentInvestigator.Api.Mcp.Server;
using EnterpriseIncidentInvestigator.Api.Observability;
using EnterpriseIncidentInvestigator.Api.Orchestration;
using Microsoft.EntityFrameworkCore;

var mcpServerIndex = Array.IndexOf(args, "--mcp-server");
if (mcpServerIndex >= 0)
{
    if (mcpServerIndex + 1 >= args.Length)
        throw new ArgumentException("An MCP server name must follow --mcp-server.");

    var mcpHost = Host.CreateApplicationBuilder(args);
    mcpHost.Logging.ClearProviders();
    var mcp = mcpHost.Services.AddMcpServer().WithStdioServerTransport();
    switch (args[mcpServerIndex + 1])
    {
        case "deployment": mcp.WithTools<DeploymentMcpTools>(); break;
        case "logs": mcp.WithTools<LogsMcpTools>(); break;
        case "metrics": mcp.WithTools<MetricsMcpTools>(); break;
        case "database": mcp.WithTools<DatabaseMcpTools>(); break;
        default: throw new ArgumentException($"Unknown MCP server '{args[mcpServerIndex + 1]}'.");
    }
    await mcpHost.Build().RunAsync();
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOptions<InvestigationOptions>()
    .Bind(builder.Configuration.GetSection(InvestigationOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<AzureOpenAIOptions>()
    .Bind(builder.Configuration.GetSection(AzureOpenAIOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<IIncidentMcpClient, IncidentMcpClient>();
builder.Services.AddDbContext<InvestigationDbContext>(
    options => options.UseSqlite(builder.Configuration.GetConnectionString("Investigations") ??
                                 "Data Source=incident-investigations.db"));
builder.Services.AddScoped<IInvestigationEventStore, SqliteInvestigationEventStore>();
builder.Services.AddSingleton<IInvestigationUpdatePublisher, SignalRInvestigationUpdatePublisher>();
builder.Services.AddScoped<ISpecializedAgent, DeploymentAgent>();
builder.Services.AddScoped<ISpecializedAgent, LogAnalysisAgent>();
builder.Services.AddScoped<ISpecializedAgent, MetricsAgent>();
builder.Services.AddScoped<ISpecializedAgent, DatabaseAgent>();
builder.Services.AddScoped<ISupervisorModel, MafSupervisorModel>();
builder.Services.AddScoped<IRootCauseAgent, RootCauseAgent>();
builder.Services.AddScoped<IIncidentInvestigator, IncidentInvestigator>();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.AllowAnyHeader()
                                                                           .AllowAnyMethod()
                                                                           .SetIsOriginAllowed(
                                                                               _ => true)
                                                                           .AllowCredentials()));
var app = builder.Build();
using (var scope = app.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<InvestigationDbContext>()
    .Database.EnsureCreatedAsync();
app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.MapHub<InvestigationHub>("/hubs/investigations");
app.Run();

public partial class Program;
