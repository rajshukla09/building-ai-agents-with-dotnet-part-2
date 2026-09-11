using ContextEngineering.Api;
using ContextEngineering.Api.Agents;
using ContextEngineering.Api.Context;
using ContextEngineering.Api.Configuration;
using ContextEngineering.Api.Orchestration;
using ContextEngineering.Api.Observability;
using ContextEngineering.Api.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Evidence")
    ?? "Data Source=context-evidence.db";

builder.Services.Configure<InvestigationOptions>(
    builder.Configuration.GetSection("Investigation"));
builder.Services.AddOptions<AzureOpenAIOptions>()
    .Bind(builder.Configuration.GetSection(AzureOpenAIOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.Configure<RetrievalPipelineOptions>(
    builder.Configuration.GetSection("RetrievalPipeline"));
builder.Services.AddDbContextFactory<EvidenceDbContext>(options =>
    options.UseSqlite(connectionString));
builder.Services.AddScoped<IEvidenceRepository, SqliteEvidenceRepository>();
builder.Services.AddSingleton<IAgentRetrievalProfileProvider, AgentRetrievalProfileProvider>();
builder.Services.AddSingleton<IRetrievalSpecificationFactory, RetrievalSpecificationFactory>();
builder.Services.AddSingleton<IEvidenceRanker, ExplainableEvidenceRanker>();
builder.Services.AddSingleton<IEvidenceDeduplicator, DeterministicEvidenceDeduplicator>();
builder.Services.AddSingleton<ISemanticEvidenceSearch, DisabledSemanticEvidenceSearch>();
builder.Services.AddSingleton<IInvestigationLanguageModel, AzureOpenAIInvestigationLanguageModel>();
builder.Services.AddSingleton<InvestigationObservabilityStore>();
builder.Services.AddSingleton<IInvestigationObserver>(services =>
    services.GetRequiredService<InvestigationObservabilityStore>());
builder.Services.AddSingleton<DemoInvestigationService>();
builder.Services.AddHostedService(services =>
    services.GetRequiredService<DemoInvestigationService>());
builder.Services.AddCors();
builder.Services.AddScoped<IContextService, EvidenceContextService>();
builder.Services.AddScoped<IInvestigationSupervisor, InvestigationSupervisor>();
builder.Services.AddSingleton<IAdditionalContextValidator, AdditionalContextValidator>();
builder.Services.AddScoped<ISpecialistAgent, LogInvestigationAgent>();
builder.Services.AddScoped<ISpecialistAgent, DeploymentAgent>();
builder.Services.AddScoped<ISpecialistAgent, DatabaseAgent>();
builder.Services.AddScoped<ISpecialistAgent, InfrastructureTelemetryAgent>();
builder.Services.AddScoped<ISpecialistAgent, ApplicationAgent>();

var app = builder.Build();

app.UseCors(policy => policy
    .AllowAnyOrigin()
    .AllowAnyHeader()
    .AllowAnyMethod());

await DatabaseInitialization.Gate.WaitAsync();
try
{
    await using var scope = app.Services.CreateAsyncScope();
    var contextFactory = scope.ServiceProvider
        .GetRequiredService<IDbContextFactory<EvidenceDbContext>>();
    await using var database = await contextFactory.CreateDbContextAsync();
    await database.Database.EnsureCreatedAsync();
}
finally
{
    DatabaseInitialization.Gate.Release();
}

app.MapGet("/health", () => Results.Ok(new { status = "ready" }));
app.MapPost(
    "/api/investigations/demo",
    async (DemoInvestigationService demo, CancellationToken cancellationToken) =>
    {
        try
        {
            var incidentId = await demo.QueueAsync(cancellationToken);
            return Results.Accepted(value: new { incidentId });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(
                title: "The demo investigation is not configured.",
                detail: exception.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    });
app.MapGet(
    "/api/investigations/{incidentId:guid}",
    async (Guid incidentId, DemoInvestigationService demo, CancellationToken cancellationToken) =>
    {
        var investigation = await demo.GetAsync(incidentId, cancellationToken);
        return investigation is null
            ? Results.NotFound()
            : Results.Ok(investigation);
    });
app.Run();

public partial class Program;

internal static class DatabaseInitialization
{
    public static SemaphoreSlim Gate { get; } = new(1, 1);
}
