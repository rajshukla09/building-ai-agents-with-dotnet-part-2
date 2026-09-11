namespace EnterpriseArchitectureAssessment.Api.Configuration;
public sealed class McpOptions
{
    public const string SectionName = "Mcp";
    public bool ReadOnly { get; set; } = true;
}
