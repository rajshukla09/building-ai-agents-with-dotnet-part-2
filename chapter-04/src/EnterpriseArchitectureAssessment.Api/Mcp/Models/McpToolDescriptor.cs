using ModelContextProtocol.Client;
namespace EnterpriseArchitectureAssessment.Api.Mcp.Models;
public sealed record McpToolDescriptor(string Server, string Name, string Description, bool ReadOnly,
                                       McpClientTool ClientTool);
