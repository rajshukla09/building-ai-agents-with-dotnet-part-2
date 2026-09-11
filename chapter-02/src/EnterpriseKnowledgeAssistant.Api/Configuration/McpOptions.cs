namespace EnterpriseKnowledgeAssistant.Api.Configuration;

public sealed class McpOptions
{
    public const string SectionName = "Mcp";
    public string ServerName { get; init; } = "enterprise-knowledge";
}
