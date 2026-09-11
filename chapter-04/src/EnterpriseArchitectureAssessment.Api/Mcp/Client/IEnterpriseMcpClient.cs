using EnterpriseArchitectureAssessment.Api.Mcp.Models;
namespace EnterpriseArchitectureAssessment.Api.Mcp.Client;
public interface IEnterpriseMcpClient
{
    IReadOnlyList<McpToolDescriptor> Discover(string server);
    Task<McpToolResult> CallAsync(McpToolCall call, CancellationToken cancellationToken = default);
    void CompleteInvocation(Guid invocationId);
}
