using ContextEngineering.Api.Models;
using ContextEngineering.Api.Persistence;
using System.Diagnostics;

namespace ContextEngineering.Api.Context;

public sealed class EvidenceContextService(
    IEvidenceRepository evidenceRepository,
    IRetrievalSpecificationFactory specificationFactory,
    IEvidenceRanker evidenceRanker,
    IEvidenceDeduplicator evidenceDeduplicator,
    ISemanticEvidenceSearch semanticSearch)
    : IContextService
{
    public async Task<ContextBundle> GetContextAsync(
        ContextRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetAgent);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CurrentGoal);

        var watch = Stopwatch.StartNew();
        var specification = specificationFactory.Create(request);
        var totalIncidentEvidence = await evidenceRepository.CountForIncidentAsync(
            request.IncidentId,
            cancellationToken);
        var candidates = await evidenceRepository.QueryAsync(specification, cancellationToken);
        var semanticCandidates = await semanticSearch.SearchAsync(
            specification,
            candidates,
            cancellationToken);
        var combinedCandidates = candidates
            .Concat(semanticCandidates)
            .GroupBy(item => item.EvidenceId)
            .Select(group => group.First())
            .ToArray();
        var ranked = evidenceRanker.Rank(
            combinedCandidates,
            specification,
            request.ExecutionState);
        var deduplicated = evidenceDeduplicator.Deduplicate(
            ranked,
            out var duplicatesRemoved);
        var selected = ApplyBudget(deduplicated, specification);

        var items = selected
            .Select(item => new ContextEvidence(
                item.Evidence.EvidenceId,
                item.Evidence.SourceAgent,
                item.Evidence.EvidenceType,
                item.Evidence.Component,
                item.Evidence.ObservedFrom,
                item.Evidence.ObservedTo,
                item.Evidence.Summary,
                item.Evidence.DetailedContent,
                specification.RetrievalReason,
                item.Score,
                item.Factors,
                true,
                item.SemanticRelevanceContributed))
            .ToArray();
        var approximateCharacters = items.Sum(item =>
            item.Summary.Length + item.DetailedContent.Length);
        watch.Stop();
        var diagnostics = new RetrievalDiagnostics(
            totalIncidentEvidence,
            candidates.Count,
            semanticCandidates.Count,
            duplicatesRemoved,
            ranked.Count,
            items.Length,
            approximateCharacters,
            specification.MaximumItems,
            specification.ContextBudget,
            watch.ElapsedMilliseconds,
            request.RetrievalIteration,
            items.Select(item => item.EvidenceId).ToArray());

        return new ContextBundle(
            request.IncidentId,
            request.TargetAgent,
            request.CurrentGoal,
            items,
            request.ExecutionState?.IncidentSummary ?? string.Empty,
            specification,
            request.ExecutionState?.ActiveHypotheses.ToArray() ?? [],
            new Dictionary<string, string>
            {
                ["targetAgent"] = request.TargetAgent,
                ["retrievalReason"] = specification.RetrievalReason
            },
            new ContextBudgetUsage(
                specification.MaximumItems,
                specification.ContextBudget,
                items.Length,
                approximateCharacters),
            diagnostics);
    }

    private static IReadOnlyList<RankedEvidence> ApplyBudget(
        IReadOnlyList<RankedEvidence> evidence,
        RetrievalSpecification specification)
    {
        var usedCharacters = 0;
        var categoryCounts = new Dictionary<EvidenceType, int>();
        var selected = new List<RankedEvidence>();

        foreach (var item in evidence)
        {
            if (selected.Count >= specification.MaximumItems)
            {
                break;
            }

            var evidenceType = item.Evidence.EvidenceType;
            categoryCounts.TryGetValue(evidenceType, out var categoryCount);
            if (specification.PerCategoryLimits is not null &&
                specification.PerCategoryLimits.TryGetValue(evidenceType, out var categoryLimit) &&
                categoryCount >= categoryLimit)
            {
                continue;
            }

            var itemSize = item.Evidence.Summary.Length + item.Evidence.DetailedContent.Length;

            if (usedCharacters + itemSize > specification.ContextBudget)
            {
                continue;
            }

            usedCharacters += itemSize;
            categoryCounts[evidenceType] = categoryCount + 1;
            selected.Add(item);
        }

        return selected;
    }
}
