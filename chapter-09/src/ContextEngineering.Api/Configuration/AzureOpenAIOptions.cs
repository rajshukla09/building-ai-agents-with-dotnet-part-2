using System.ComponentModel.DataAnnotations;

namespace ContextEngineering.Api.Configuration;

public sealed class AzureOpenAIOptions
{
    public const string SectionName = "AzureOpenAI";

    [Required]
    public string Endpoint { get; set; } = "https://example.openai.azure.com";

    [Required]
    public string ApiKey { get; set; } = "configure-with-user-secrets";

    [Required]
    public string DeploymentName { get; set; } = "gpt-4o-mini";

    public bool UseLiveModel { get; set; } = true;
}
