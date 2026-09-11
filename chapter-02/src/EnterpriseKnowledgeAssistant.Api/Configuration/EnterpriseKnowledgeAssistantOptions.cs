using System.ComponentModel.DataAnnotations;

namespace EnterpriseKnowledgeAssistant.Api.Configuration;

public sealed class EnterpriseKnowledgeAssistantOptions
{
    public const string SectionName = "EnterpriseKnowledgeAssistant";
    [Range(1, 20)] public int MaxToolCalls { get; init; } = 5;
}

public sealed class AzureOpenAIOptions
{
    public const string SectionName = "AzureOpenAI";
    [Required] public string Endpoint { get; init; } = "";
    [Required] public string ApiKey { get; init; } = "";
    [Required] public string DeploymentName { get; init; } = "";
}
