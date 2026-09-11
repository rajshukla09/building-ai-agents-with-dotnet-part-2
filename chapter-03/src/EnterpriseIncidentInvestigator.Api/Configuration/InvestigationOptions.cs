using System.ComponentModel.DataAnnotations;

namespace EnterpriseIncidentInvestigator.Api.Configuration;

public sealed class InvestigationOptions
{
    public const string SectionName = "IncidentInvestigation";
    [Range(1, 20)] public int MaxAgentInvocations { get; set; } = 8;
    [Range(1, 20)] public int MaxToolCallsPerAgent { get; set; } = 5;
}

public sealed class AzureOpenAIOptions
{
    public const string SectionName = "AzureOpenAI";
    [Required] public string Endpoint { get; set; } = "https://example.openai.azure.com";
    [Required] public string ApiKey { get; set; } = "configure-with-user-secrets";
    [Required] public string DeploymentName { get; set; } = "gpt-4o-mini";
}
