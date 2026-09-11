using Azure;
using Azure.AI.OpenAI;
using EnterpriseIncidentInvestigator.Api.Configuration;
using EnterpriseIncidentInvestigator.Api.Contracts;
using EnterpriseIncidentInvestigator.Api.Orchestration;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace EnterpriseIncidentInvestigator.Api.Agents;

public sealed class RootCauseAgent(IOptions<AzureOpenAIOptions> options) : IRootCauseAgent
{
    public async Task<RootCauseConclusion> SynthesizeAsync(IncidentInvestigationContext context, bool limitReached,
                                                           CancellationToken cancellationToken)
    {
        AIAgent agent =
            new AzureOpenAIClient(new Uri(options.Value.Endpoint), new AzureKeyCredential(options.Value.ApiKey))
                .GetChatClient(options.Value.DeploymentName)
                .AsAIAgent(
                    name: "RootCauseAgent",
                    instructions: ("Synthesize only the supplied evidence. Format four lines: Summary:, Likely Root " +
                                   "Cause:, Confidence: 0-1, Recommended Action:. Do not investigate or call tools."));
        var evidence = string.Join("\n", context.Findings.Select(x => $"{x.AgentName}: {x.Summary}"));
        var response = await agent.RunAsync(
            $"Incident: {context.OriginalIncident}\nEvidence:\n{evidence}\nInvocation limit reached: {limitReached}",
            cancellationToken: cancellationToken);
        return Parse(response.Text);
    }

    private static RootCauseConclusion Parse(string text)
    {
        string Line(string label) =>
            text.Split('\n')
                .FirstOrDefault(x => x.TrimStart().StartsWith(label, StringComparison.OrdinalIgnoreCase))
                ?.Split(':', 2)
                .ElementAtOrDefault(1)
                ?.Trim() ??
            "Unknown";
        return new(Line("Summary"), Line("Likely Root Cause"),
                   double.TryParse(Line("Confidence"), out var confidence) ? Math.Clamp(confidence, 0, 1) : 0.5,
                   Line("Recommended Action"));
    }
}
