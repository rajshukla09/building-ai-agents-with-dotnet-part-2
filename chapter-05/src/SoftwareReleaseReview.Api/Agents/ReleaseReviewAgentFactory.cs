using Azure;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using OpenAI.Chat;

namespace SoftwareReleaseReview.Api.Agents;

public sealed class ReleaseReviewAgentFactory(IConfiguration configuration)
{
    public AIAgent Create(string name, string description, string instructions)
    {
        var endpoint = configuration["AzureOpenAI:Endpoint"]
            ?? throw new InvalidOperationException("Configure AzureOpenAI:Endpoint.");
        var key = configuration["AzureOpenAI:ApiKey"]
            ?? throw new InvalidOperationException("Configure AzureOpenAI:ApiKey.");
        var deployment = configuration["AzureOpenAI:DeploymentName"]
            ?? throw new InvalidOperationException("Configure AzureOpenAI:DeploymentName.");

        var chat = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(key))
            .GetChatClient(deployment);

        return chat.AsAIAgent(new ChatClientAgentOptions
        {
            Name = name,
            Description = description,
            ChatOptions = new()
            {
                Instructions = $"{instructions} Be concise. State evidence and a recommendation; do not reveal chain-of-thought."
            }
        });
    }
}
