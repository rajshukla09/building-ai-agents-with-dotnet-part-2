using Azure;
using Azure.AI.OpenAI;
using EnterpriseArchitectureAssessment.Api.Mcp.Client;
using EnterpriseArchitectureAssessment.Api.Mcp.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using System.Text.Json;
namespace EnterpriseArchitectureAssessment.Api.Agents;
public abstract class AssessmentAgentFactory(IEnterpriseMcpClient mcp, IConfiguration configuration)
{
    public abstract string Name { get; }
    protected abstract string Server { get; }
    protected abstract string Description { get; }
    protected abstract string Instructions { get; }
    public AIAgent Create()
    {
        var chat = new AzureOpenAIClient(
                       new Uri(configuration["AzureOpenAI:Endpoint"] ??
                               throw new InvalidOperationException("Configure AzureOpenAI:Endpoint")),
                       new AzureKeyCredential(configuration["AzureOpenAI:ApiKey"] ??
                                              throw new InvalidOperationException("Configure AzureOpenAI:ApiKey")))
                       .GetChatClient(configuration["AzureOpenAI:DeploymentName"] ??
                                      throw new InvalidOperationException("Configure AzureOpenAI:DeploymentName"));
        var tools = mcp.Discover(Server)
                        .Select(tool => AIFunctionFactory.Create(
                                    async (string query, CancellationToken ct) =>
                                    {
                                        // MAF chooses the tool; this adapter validates and executes it through the MCP
                                        // boundary.
                                        using var document =
                                            JsonDocument.Parse(JsonSerializer.Serialize(new { query }));
                                        var invocation = AgentInvocationContext.Current;
                                        return (await mcp.CallAsync(new McpToolCall(invocation, Server, tool.Name,
                                                                                    document.RootElement.Clone()),
                                                                    ct))
                                            .Content;
                                    },
                                    name: tool.Name, description: tool.Description))
                        .ToArray();
        return chat.AsAIAgent(new ChatClientAgentOptions {
            Name = Name, Description = Description,
            ChatOptions =
                new() {
                    Instructions =
                        $"{Instructions} Select only relevant advertised MCP tools. Return a concise evidence-based finding; do not reveal chain-of-thought.",
                    Tools = [..tools]
                }
        });
    }
}
public static class AgentInvocationContext
{
    private static readonly AsyncLocal<Guid> Value = new();
    public static Guid Current
    {
        get {
            if (Value.Value == Guid.Empty)
                Value.Value = Guid.NewGuid();
            return Value.Value;
        }
    }
    public static void Reset() => Value.Value = Guid.Empty;
}
