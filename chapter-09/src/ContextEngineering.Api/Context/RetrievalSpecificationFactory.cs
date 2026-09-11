using ContextEngineering.Api.Models;

namespace ContextEngineering.Api.Context;

public interface IRetrievalSpecificationFactory
{
    RetrievalSpecification Create(ContextRequest request);
}

public sealed class RetrievalSpecificationFactory(
    IAgentRetrievalProfileProvider profileProvider) : IRetrievalSpecificationFactory
{
    public RetrievalSpecification Create(ContextRequest request)
    {
        var profile = profileProvider.GetProfile(request.TargetAgent);
        var evidenceTypes = IntersectOrDefault(
            profile.EvidenceTypes,
            request.AllowedEvidenceTypes);
        var sourceAgents = IntersectOrDefault(
            profile.SourceAgents,
            request.SourceAgents);
        var components = GetComponents(request);

        return new RetrievalSpecification(
            request.IncidentId,
            request.TargetAgent,
            request.CurrentGoal,
            evidenceTypes,
            sourceAgents,
            components,
            request.From ?? request.ExecutionState?.IncidentFrom,
            request.To ?? request.ExecutionState?.IncidentTo,
            Math.Min(Math.Clamp(request.MaximumItems, 1, 100), profile.MaximumItems),
            profile.ContextBudget,
            profile.RetrievalReason,
            profile.PerCategoryLimits,
            profile.MaximumEvidenceAge);
    }

    private static IReadOnlyCollection<T> IntersectOrDefault<T>(
        IReadOnlyCollection<T> profileValues,
        IReadOnlyCollection<T>? requestedValues)
    {
        if (requestedValues is not { Count: > 0 })
        {
            return profileValues;
        }

        return profileValues.Intersect(requestedValues).ToArray();
    }

    private static IReadOnlyCollection<string> GetComponents(ContextRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Component))
        {
            return [request.Component];
        }

        return request.ExecutionState?.AffectedComponents.ToArray() ?? [];
    }
}
