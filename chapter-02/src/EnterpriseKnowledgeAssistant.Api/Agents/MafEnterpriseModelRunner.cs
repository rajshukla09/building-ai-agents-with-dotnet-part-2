using Azure;
using Azure.AI.OpenAI;
using EnterpriseKnowledgeAssistant.Api.Configuration;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace EnterpriseKnowledgeAssistant.Api.Agents;

public sealed class MafEnterpriseModelRunner(IOptions<AzureOpenAIOptions> options) : IEnterpriseModelRunner
{
    public async Task<string> RunAsync(string query, IReadOnlyList<ModelTool> tools, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var functions = tools.Select(tool => AIFunctionFactory.Create(
            (string query, CancellationToken ct) => tool.InvokeAsync(query, ct),
            name: tool.Name,
            description: tool.Description)).ToArray();

        AIAgent agent = new AzureOpenAIClient(new Uri(settings.Endpoint), new AzureKeyCredential(settings.ApiKey))
            .GetChatClient(settings.DeploymentName)
            .AsAIAgent(new ChatClientAgentOptions
            {
                Name = "EnterpriseKnowledgeAgent",
                ChatOptions = new ChatOptions
                {
                    Instructions = EnterpriseAgentInstructions.SystemPrompt,
                    Tools = [.. functions]
                }
            });

        AgentResponse response = await agent.RunAsync(query, cancellationToken: cancellationToken);
        return response.Text;
    }
}
