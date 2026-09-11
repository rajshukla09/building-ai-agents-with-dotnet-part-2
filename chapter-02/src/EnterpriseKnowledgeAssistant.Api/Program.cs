using EnterpriseKnowledgeAssistant.Api.Agents;
using EnterpriseKnowledgeAssistant.Api.Configuration;
using EnterpriseKnowledgeAssistant.Api.Mcp.Client;
using EnterpriseKnowledgeAssistant.Api.Mcp.Server;

if (args.Contains("--mcp-server", StringComparer.Ordinal))
{
    var mcpHost = Host.CreateApplicationBuilder(args);
    mcpHost.Logging.ClearProviders();
    mcpHost.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithTools<EnterpriseMcpTools>();
    await mcpHost.Build().RunAsync();
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOptions<McpOptions>()
    .Bind(builder.Configuration.GetSection(McpOptions.SectionName))
    .Validate(x => !string.IsNullOrWhiteSpace(x.ServerName), "Configure the MCP server name.")
    .ValidateOnStart();
builder.Services.AddOptions<EnterpriseKnowledgeAssistantOptions>()
    .Bind(builder.Configuration.GetSection(EnterpriseKnowledgeAssistantOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<AzureOpenAIOptions>()
    .Bind(builder.Configuration.GetSection(AzureOpenAIOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<IMcpClient, EnterpriseMcpClient>();
builder.Services.AddScoped<IEnterpriseModelRunner, MafEnterpriseModelRunner>();
builder.Services.AddScoped<IEnterpriseAssistantAgent, EnterpriseAssistantAgent>();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.Run();
public partial class Program;
