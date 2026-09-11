using Azure;
using Azure.AI.OpenAI;
using EnterpriseIncidentInvestigator.Api.Configuration;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace EnterpriseIncidentInvestigator.Api.Agents;

public sealed class MafSupervisorModel(IOptions<AzureOpenAIOptions> options) : ISupervisorModel
{
    public async Task<string> RunAsync(string incident, string context, IReadOnlyList<AgentDelegate> agents, CancellationToken cancellationToken)
    {
        var tools = agents.Select(candidate => AIFunctionFactory.Create(
            (string assignment, string reasonSelected, CancellationToken ct) => candidate.InvokeAsync(assignment, reasonSelected, ct),
            name: candidate.Descriptor.Name,
            description: candidate.Descriptor.Description)).ToArray();
        AIAgent supervisor = new AzureOpenAIClient(new Uri(options.Value.Endpoint), new AzureKeyCredential(options.Value.ApiKey)).GetChatClient(options.Value.DeploymentName).AsAIAgent(new ChatClientAgentOptions
        {
            Name = "SupervisorAgent",
            ChatOptions = new ChatOptions { Instructions = Instructions, Tools = [.. tools] }
        });
        var response = await supervisor.RunAsync($"Incident: {incident}\nShared investigation context: {context}", cancellationToken: cancellationToken);
        return response.Text;
    }

    private const string Instructions = """
        You supervise an incident investigation; you do not investigate directly.
        Dynamically decide WHO should work by selecting agents solely from their descriptions, the incident, prior findings, and remaining uncertainty. There is no fixed order or keyword router.
        Give every delegation a focused assignment and a short actionable reasonSelected (never private chain-of-thought). You may invoke the same agent again only when new evidence makes a materially different assignment useful.
        Continue delegating until evidence is sufficient. Then return a brief completion note. Deterministic invocation limits are enforced outside the model.
        """;
}
