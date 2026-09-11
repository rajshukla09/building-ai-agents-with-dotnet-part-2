using System.Text.Json;
namespace EnterpriseArchitectureAssessment.Api.Mcp.Models;
public sealed record McpToolCall(Guid InvocationId, string Server, string Tool, JsonElement Arguments);
public sealed record McpToolResult(string Content, bool FromCache = false);
