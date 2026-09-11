using ContextEngineering.Api.Agents;
using ContextEngineering.Api.Configuration;
using ContextEngineering.Api.Context;
using ContextEngineering.Api.Models;
using ContextEngineering.Api.Observability;
using ContextEngineering.Api.Orchestration;
using ContextEngineering.Api.Persistence;
using Microsoft.Extensions.Options;
using System.Threading.Channels;

namespace ContextEngineering.Api;

public sealed class DemoInvestigationService(
    IServiceScopeFactory scopeFactory,
    InvestigationObservabilityStore observabilityStore,
    IOptions<AzureOpenAIOptions> languageModelOptions,
    ILogger<DemoInvestigationService> logger) : BackgroundService
{
    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    public async Task<Guid> QueueAsync(CancellationToken cancellationToken)
    {
        ValidateConfiguration();
        var incidentId = Guid.NewGuid();
        observabilityStore.Start(
            incidentId,
            $"Incident {incidentId}: Explain checkout latency after deployment and recommend mitigation.");
        await _queue.Writer.WriteAsync(incidentId, cancellationToken);
        return incidentId;
    }

    public async Task<InvestigationView?> GetAsync(
        Guid incidentId,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IEvidenceRepository>();
        return await observabilityStore.GetAsync(incidentId, repository, cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var incidentId in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await RunAsync(incidentId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                observabilityStore.Fail(incidentId, "The investigation stopped because the API host is shutting down.");
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Demo investigation {IncidentId} failed",
                    incidentId);
                observabilityStore.Fail(
                    incidentId,
                    "Investigation execution failed. Verify Azure OpenAI endpoint, deployment, credentials, and service availability.");
            }
        }
    }

    private async Task RunAsync(Guid incidentId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var repository = services.GetRequiredService<IEvidenceRepository>();
        var languageModel = services.GetRequiredService<IInvestigationLanguageModel>();
        await SeedEvidenceAsync(repository, incidentId, cancellationToken);

        ISpecialistAgent[] agents =
        [
            new DemoAgent(nameof(DeploymentAgent), EvidenceType.Deployment, "checkout-api", "Release v4.8 preceded the latency increase.", languageModel),
            new DemoAgent(nameof(LogInvestigationAgent), EvidenceType.Log, "checkout-api", "Connection-pool exhaustion warnings followed release v4.8.", languageModel),
            new DemoDatabaseAgent(languageModel),
            new DemoAgent(nameof(InfrastructureTelemetryAgent), EvidenceType.Telemetry, "checkout-platform", "Host capacity remained healthy during the incident.", languageModel),
            new DemoAgent(nameof(ApplicationAgent), EvidenceType.Application, "checkout-api", "Release v4.8 reduced the database connection-pool maximum.", languageModel)
        ];
        var supervisor = new InvestigationSupervisor(
            agents,
            services.GetRequiredService<IContextService>(),
            repository,
            services.GetRequiredService<IOptions<InvestigationOptions>>(),
            services.GetRequiredService<IAdditionalContextValidator>(),
            observabilityStore);

        var state = await supervisor.InvestigateAsync(
            incidentId,
            "Explain checkout latency after deployment and recommend mitigation.",
            [
                new InvestigationAssignment(nameof(DeploymentAgent), "Confirm release timing and affected version.", Component: "checkout-api"),
                new InvestigationAssignment(nameof(LogInvestigationAgent), "Correlate warnings with the deployment.", Component: "checkout-api"),
                new InvestigationAssignment(nameof(DatabaseAgent), "Measure database connection waits.", Component: "checkout-api"),
                new InvestigationAssignment(nameof(InfrastructureTelemetryAgent), "Exclude infrastructure saturation.", Component: "checkout-platform"),
                new InvestigationAssignment(nameof(ApplicationAgent), "Inspect connection-pool configuration changes.", Component: "checkout-api")
            ],
            cancellationToken);

        var recommendation = await languageModel.GenerateRecommendationAsync(
            state,
            cancellationToken);
        observabilityStore.Complete(incidentId, recommendation);
    }

    private static async Task SeedEvidenceAsync(
        IEvidenceRepository repository,
        Guid incidentId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var categories = Enum.GetValues<EvidenceType>();
        for (var index = 0; index < 45; index++)
        {
            var type = categories[index % categories.Length];
            var source = SourceFor(type);
            var component = index % 4 == 0
                ? "catalog-api"
                : type == EvidenceType.Database
                    ? "orders-database"
                    : type == EvidenceType.Telemetry
                        ? "checkout-platform"
                        : "checkout-api";
            var duplicateGroup = index is 10 or 15 or 20;
            var summary = duplicateGroup
                ? "Repeated checkout timeout observation"
                : $"Demo {type} observation {index}";
            await repository.AddAsync(
                new EvidenceRecord
                {
                    EvidenceId = Guid.NewGuid(),
                    IncidentId = incidentId,
                    SourceAgent = source,
                    EvidenceType = type,
                    Component = component,
                    ObservedFrom = now.AddMinutes(-60 + index),
                    ObservedTo = now.AddMinutes(-59 + index),
                    Summary = summary,
                    DetailedContent = $"Deterministic demo detail for {type} evidence {index} on {component}. " +
                        $"Observed request samples and correlated telemetry: {new string('x', 240)}",
                    Importance = index % 9 == 0 ? 5 : 2,
                    CreatedAt = now.AddSeconds(index)
                },
                cancellationToken);
        }
    }

    private void ValidateConfiguration()
    {
        var configuration = languageModelOptions.Value;
        if (!configuration.UseLiveModel)
        {
            return;
        }

        if (!Uri.TryCreate(configuration.Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps ||
            configuration.Endpoint.Contains("example.openai.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "AzureOpenAI:Endpoint must be a configured absolute HTTPS Azure OpenAI endpoint.");
        }

        if (string.IsNullOrWhiteSpace(configuration.DeploymentName) ||
            configuration.DeploymentName.Contains("YOUR-", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "AzureOpenAI:DeploymentName must identify a configured Azure OpenAI deployment.");
        }

        if (string.IsNullOrWhiteSpace(configuration.ApiKey) ||
            configuration.ApiKey == "configure-with-user-secrets")
        {
            throw new InvalidOperationException(
                "AzureOpenAI:ApiKey must be supplied through user secrets or environment configuration.");
        }
    }

    private static string SourceFor(EvidenceType type)
    {
        return type switch
        {
            EvidenceType.Log => nameof(LogInvestigationAgent),
            EvidenceType.Deployment => nameof(DeploymentAgent),
            EvidenceType.Database => nameof(DatabaseAgent),
            EvidenceType.Telemetry => nameof(InfrastructureTelemetryAgent),
            EvidenceType.Application => nameof(ApplicationAgent),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
    }

    private class DemoAgent(
        string name,
        EvidenceType type,
        string component,
        string detail,
        IInvestigationLanguageModel languageModel) : ISpecialistAgent
    {
        public string Name { get; } = name;

        public virtual async Task<SpecialistFinding> InvestigateAsync(
            SpecialistRequest request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(120, cancellationToken);
            return await languageModel.GenerateFindingAsync(
                Name,
                type,
                component,
                request,
                detail,
                cancellationToken);
        }
    }

    private sealed class DemoDatabaseAgent(IInvestigationLanguageModel languageModel)
        : DemoAgent(
            nameof(DatabaseAgent),
            EvidenceType.Database,
            "orders-database",
            "Database connection acquisition waits increased after release v4.8.",
            languageModel)
    {
        public override async Task<SpecialistFinding> InvestigateAsync(
            SpecialistRequest request,
            CancellationToken cancellationToken)
        {
            var finding = await base.InvestigateAsync(request, cancellationToken);
            var isFirstRetrieval = request.Constraints["retrievalIteration"] == "1";
            return isFirstRetrieval
                ? finding with
                {
                    AdditionalContextRequired = new AdditionalContextRequirement(
                        EvidenceType.Database,
                        "orders-database",
                        DateTimeOffset.UtcNow.AddHours(-1),
                        DateTimeOffset.UtcNow,
                        "Connection acquisition wait measurements",
                        "Confirm whether pool exhaustion reached the database.",
                        ["connection", "wait", "pool"])
                }
                : finding;
        }
    }
}
