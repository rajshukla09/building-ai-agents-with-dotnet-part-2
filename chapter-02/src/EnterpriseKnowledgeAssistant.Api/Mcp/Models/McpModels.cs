using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Client;

namespace EnterpriseKnowledgeAssistant.Api.Mcp.Models;

public sealed record McpServerDescriptor(string Name, string Description, string Transport, bool Connected);
public sealed record McpToolDescriptor(string Server, string Name, string Description,
                                       JsonElement InputSchema)
{
    [JsonIgnore]
    public McpClientTool ClientTool { get; init; } = null!;
}
public sealed record McpToolCall(string Server, string Tool, IReadOnlyDictionary<string, JsonElement> Arguments);
public sealed record McpToolResult(string Server, string Tool, JsonElement Content, bool Succeeded);
