using Azure;
using Azure.AI.OpenAI;
using ContextEngineering.Api.Configuration;
using ContextEngineering.Api.Models;
using ContextEngineering.Api.Observability;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace ContextEngineering.Api.Agents;

public interface IInvestigationLanguageModel
{
    Task<SpecialistFinding> GenerateFindingAsync(
        string agentName,
        EvidenceType evidenceType,
        string component,
        SpecialistRequest request,
        string deterministicDetail,
        CancellationToken cancellationToken);

    Task<FinalRecommendationView> GenerateRecommendationAsync(
        SupervisorExecutionState state,
        CancellationToken cancellationToken);
}

public sealed class AzureOpenAIInvestigationLanguageModel(
    IOptions<AzureOpenAIOptions> options) : IInvestigationLanguageModel
{
    public async Task<SpecialistFinding> GenerateFindingAsync(
        string agentName,
        EvidenceType evidenceType,
        string component,
        SpecialistRequest request,
        string deterministicDetail,
        CancellationToken cancellationToken)
    {
        if (!options.Value.UseLiveModel)
        {
            return DeterministicFinding(
                agentName,
                evidenceType,
                component,
                deterministicDetail);
        }

        var agent = CreateAgent(
            agentName,
            $"""
            You are the {agentName}. Analyze only the supplied structured Context Bundle.
            Do not invent evidence or expose hidden reasoning. Return exactly four lines:
            Summary: a concise finding
            Detail: an evidence-grounded explanation
            Hypothesis: a compact routing hypothesis
            StatusScore: an uncalibrated number from 0 to 1
            """);
        var evidence = string.Join(
            "\n",
            request.Context.Evidence.Select(item =>
                $"[{item.EvidenceId}] {item.EvidenceType} {item.Component}: " +
                $"{item.Summary} | {item.DetailedContent}"));
        var response = await agent.RunAsync(
            $"""
            Incident: {request.Context.IncidentSummary}
            Task: {request.CurrentTask}
            Selected evidence:
            {evidence}
            """,
            cancellationToken: cancellationToken);

        return new SpecialistFinding(
            agentName,
            evidenceType,
            component,
            request.Context.Evidence.Select(item => item.ObservedFrom).Min(),
            request.Context.Evidence.Select(item => item.ObservedTo).Max(),
            ReadLine(response.Text, "Summary", $"{agentName} completed the investigation."),
            ReadLine(response.Text, "Detail", deterministicDetail),
            ReadLine(response.Text, "Hypothesis", $"{component} may contribute to the incident."),
            ReadScore(response.Text));
    }

    public async Task<FinalRecommendationView> GenerateRecommendationAsync(
        SupervisorExecutionState state,
        CancellationToken cancellationToken)
    {
        if (!options.Value.UseLiveModel)
        {
            return new FinalRecommendationView(
                "Release v4.8 reduced the checkout database pool size, causing elevated connection waits and latency.",
                "Restore the prior pool limit, deploy the correction, and monitor connection waits and checkout latency.",
                state.Status.ToString(),
                state.Confidence,
                state.EvidenceReferences.Select(item => item.EvidenceId).ToArray());
        }

        var agent = CreateAgent(
            "RootCauseRecommendationAgent",
            """
            Synthesize only the supplied compact hypotheses and evidence references.
            Do not expose hidden reasoning. Return exactly two lines:
            RootCause: a concise likely root cause
            Recommendation: a concrete mitigation and verification action
            """);
        var response = await agent.RunAsync(
            $"""
            Goal: {state.CurrentGoal}
            Hypotheses: {string.Join(" | ", state.ActiveHypotheses.Select(item => item.Description))}
            Evidence references: {string.Join(" | ", state.EvidenceReferences.Select(item => $"{item.EvidenceId}: {item.Summary}"))}
            """,
            cancellationToken: cancellationToken);

        return new FinalRecommendationView(
            ReadLine(response.Text, "RootCause", "The evidence supports a deployment-related pool regression."),
            ReadLine(response.Text, "Recommendation", "Restore prior pool settings and verify latency."),
            state.Status.ToString(),
            state.Confidence,
            state.EvidenceReferences.Select(item => item.EvidenceId).ToArray());
    }

    private AIAgent CreateAgent(string name, string instructions)
    {
        return new AzureOpenAIClient(
                new Uri(options.Value.Endpoint),
                new AzureKeyCredential(options.Value.ApiKey))
            .GetChatClient(options.Value.DeploymentName)
            .AsAIAgent(name: name, instructions: instructions);
    }

    private static SpecialistFinding DeterministicFinding(
        string agentName,
        EvidenceType evidenceType,
        string component,
        string detail)
    {
        return new SpecialistFinding(
            agentName,
            evidenceType,
            component,
            null,
            null,
            $"{agentName} produced a structured finding.",
            detail,
            detail,
            0.78);
    }

    private static string ReadLine(string text, string label, string fallback)
    {
        return text.Split('\n')
            .FirstOrDefault(line => line.TrimStart().StartsWith(
                $"{label}:",
                StringComparison.OrdinalIgnoreCase))
            ?.Split(':', 2)
            .ElementAtOrDefault(1)
            ?.Trim() ?? fallback;
    }

    private static double ReadScore(string text)
    {
        return double.TryParse(ReadLine(text, "StatusScore", "0.5"), out var score)
            ? Math.Clamp(score, 0, 1)
            : 0.5;
    }
}
