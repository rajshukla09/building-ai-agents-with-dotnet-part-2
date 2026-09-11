using ContextEngineering.Api.Context;
using ContextEngineering.Api.Models;

namespace ContextEngineering.Api.Orchestration;

public sealed record AdditionalContextValidationResult(bool IsValid, string? Error);

public interface IAdditionalContextValidator
{
    AdditionalContextValidationResult Validate(
        string agentName,
        AdditionalContextRequirement requirement);
}

public sealed class AdditionalContextValidator(
    IAgentRetrievalProfileProvider profileProvider) : IAdditionalContextValidator
{
    public AdditionalContextValidationResult Validate(
        string agentName,
        AdditionalContextRequirement requirement)
    {
        var profile = profileProvider.GetProfile(agentName);

        if (!profile.EvidenceTypes.Contains(requirement.RequiredEvidenceType))
        {
            return new(false, "The requested evidence category is outside the Agent retrieval profile.");
        }

        if (string.IsNullOrWhiteSpace(requirement.Component) ||
            string.IsNullOrWhiteSpace(requirement.MissingInformation) ||
            string.IsNullOrWhiteSpace(requirement.Reason))
        {
            return new(false, "Component, missing information, and reason are required.");
        }

        if (requirement.From is not null &&
            requirement.To is not null &&
            requirement.From > requirement.To)
        {
            return new(false, "The additional-context time range is invalid.");
        }

        if (requirement.Keywords is { Count: > 10 } ||
            requirement.Keywords?.Any(keyword => keyword.Length > 100) == true)
        {
            return new(false, "The additional-context keyword constraint is too large.");
        }

        return new(true, null);
    }
}
