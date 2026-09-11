using ContextEngineering.Api.Agents;
using ContextEngineering.Api.Models;
using Microsoft.Extensions.Options;

namespace ContextEngineering.Api.Context;

public sealed record AgentRetrievalProfile(
    string AgentName,
    IReadOnlyCollection<EvidenceType> EvidenceTypes,
    IReadOnlyCollection<string> SourceAgents,
    int MaximumItems,
    int ContextBudget,
    string RetrievalReason,
    IReadOnlyDictionary<EvidenceType, int>? PerCategoryLimits = null,
    TimeSpan? MaximumEvidenceAge = null);

public sealed class RetrievalPipelineOptions
{
    public int MaximumItemsPerBundle { get; set; } = 20;

    public int MaximumCharactersPerBundle { get; set; } = 8_000;

    public int MaximumItemsPerCategory { get; set; } = 8;

    public int MaximumEvidenceAgeHours { get; set; } = 168;
}

public interface IAgentRetrievalProfileProvider
{
    AgentRetrievalProfile GetProfile(string agentName);
}

public sealed class AgentRetrievalProfileProvider(
    IOptions<RetrievalPipelineOptions>? configuredOptions = null) : IAgentRetrievalProfileProvider
{
    private static readonly IReadOnlyDictionary<string, AgentRetrievalProfile> Profiles =
        new Dictionary<string, AgentRetrievalProfile>(StringComparer.Ordinal)
        {
            [nameof(LogInvestigationAgent)] = new(
                nameof(LogInvestigationAgent),
                [EvidenceType.Deployment, EvidenceType.Log, EvidenceType.Application],
                [nameof(DeploymentAgent), nameof(LogInvestigationAgent), nameof(ApplicationAgent)],
                20,
                8_000,
                "Deployment correlation, prior log findings, and application symptoms relevant to log analysis."),
            [nameof(DatabaseAgent)] = new(
                nameof(DatabaseAgent),
                [EvidenceType.Database, EvidenceType.Log, EvidenceType.Application, EvidenceType.Deployment],
                [nameof(DatabaseAgent), nameof(LogInvestigationAgent), nameof(ApplicationAgent), nameof(DeploymentAgent)],
                16,
                6_000,
                "Database findings and selected log or application evidence suggesting database impact."),
            [nameof(DeploymentAgent)] = new(
                nameof(DeploymentAgent),
                [EvidenceType.Deployment, EvidenceType.Application, EvidenceType.Telemetry],
                [nameof(DeploymentAgent), nameof(ApplicationAgent), nameof(InfrastructureTelemetryAgent)],
                16,
                6_000,
                "Deployment timeline, versions, affected services, incident timing, and related observations."),
            [nameof(InfrastructureTelemetryAgent)] = new(
                nameof(InfrastructureTelemetryAgent),
                [EvidenceType.Telemetry, EvidenceType.Deployment, EvidenceType.Application],
                [nameof(InfrastructureTelemetryAgent), nameof(DeploymentAgent), nameof(ApplicationAgent)],
                18,
                7_000,
                "Platform telemetry with deployment and application signals for the affected service."),
            [nameof(ApplicationAgent)] = new(
                nameof(ApplicationAgent),
                [EvidenceType.Application, EvidenceType.Log, EvidenceType.Deployment, EvidenceType.Database],
                [nameof(ApplicationAgent), nameof(LogInvestigationAgent), nameof(DeploymentAgent), nameof(DatabaseAgent)],
                20,
                8_000,
                "Application behavior correlated with logs, deployments, and downstream database observations.")
        };

    public AgentRetrievalProfile GetProfile(string agentName)
    {
        if (!Profiles.TryGetValue(agentName, out var profile))
        {
            throw new InvalidOperationException(
                $"No retrieval profile is configured for specialist '{agentName}'.");
        }

        var options = configuredOptions?.Value ?? new RetrievalPipelineOptions();
        var categoryLimits = profile.EvidenceTypes.ToDictionary(
            evidenceType => evidenceType,
            _ => options.MaximumItemsPerCategory);

        return profile with
        {
            MaximumItems = Math.Min(profile.MaximumItems, options.MaximumItemsPerBundle),
            ContextBudget = Math.Min(profile.ContextBudget, options.MaximumCharactersPerBundle),
            PerCategoryLimits = categoryLimits,
            MaximumEvidenceAge = TimeSpan.FromHours(options.MaximumEvidenceAgeHours)
        };
    }
}
