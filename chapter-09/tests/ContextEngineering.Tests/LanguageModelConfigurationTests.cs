using ContextEngineering.Api.Agents;
using ContextEngineering.Api.Models;
using ContextEngineering.Api.Observability;
using Xunit;

namespace ContextEngineering.Tests;

public sealed class LanguageModelConfigurationTests
{
    [Fact]
    public async Task Every_specialist_uses_the_common_language_model_service()
    {
        var languageModel = new RecordingLanguageModel();
        ISpecialistAgent[] agents =
        [
            new LogInvestigationAgent(languageModel),
            new DeploymentAgent(languageModel),
            new DatabaseAgent(languageModel),
            new InfrastructureTelemetryAgent(languageModel),
            new ApplicationAgent(languageModel)
        ];
        var incidentId = Guid.NewGuid();

        foreach (var agent in agents)
        {
            await agent.InvestigateAsync(
                new SpecialistRequest(
                    incidentId,
                    "Investigate checkout latency.",
                    new ContextBundle(
                        incidentId,
                        agent.Name,
                        "Investigate checkout latency.",
                        []),
                    new Dictionary<string, string>()),
                default);
        }

        Assert.Equal(agents.Select(agent => agent.Name), languageModel.AgentNames);
    }

    private sealed class RecordingLanguageModel : IInvestigationLanguageModel
    {
        public List<string> AgentNames { get; } = [];

        public Task<SpecialistFinding> GenerateFindingAsync(
            string agentName,
            EvidenceType evidenceType,
            string component,
            SpecialistRequest request,
            string deterministicDetail,
            CancellationToken cancellationToken)
        {
            AgentNames.Add(agentName);
            return Task.FromResult(new SpecialistFinding(
                agentName,
                evidenceType,
                component,
                null,
                null,
                "summary",
                "detail",
                "hypothesis",
                0.5));
        }

        public Task<FinalRecommendationView> GenerateRecommendationAsync(
            SupervisorExecutionState state,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new FinalRecommendationView(
                "cause",
                "recommendation",
                "completed",
                0.5,
                []));
        }
    }
}
