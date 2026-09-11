using EnterpriseKnowledgeAssistant.Api.Mcp.Models;

namespace EnterpriseKnowledgeAssistant.Api.Mcp.Client;

public interface IMcpClient
{
    IReadOnlyList<McpServerDescriptor> Servers { get; }
    Task<IReadOnlyList<McpToolDescriptor>> DiscoverToolsAsync(CancellationToken cancellationToken = default);
    Task<McpToolResult> InvokeAsync(McpToolCall call, IReadOnlyList<McpToolDescriptor> requestCatalog, CancellationToken cancellationToken = default);
}
