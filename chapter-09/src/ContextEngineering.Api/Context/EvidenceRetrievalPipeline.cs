using ContextEngineering.Api.Models;
using ContextEngineering.Api.Persistence;

namespace ContextEngineering.Api.Context;

public sealed record RankedEvidence(
    EvidenceRecord Evidence,
    double Score,
    IReadOnlyList<RankingFactor> Factors,
    bool SemanticRelevanceContributed = false);

public interface IEvidenceRanker
{
    IReadOnlyList<RankedEvidence> Rank(
        IReadOnlyCollection<EvidenceRecord> candidates,
        RetrievalSpecification specification,
        SupervisorExecutionState? state);
}

public sealed class ExplainableEvidenceRanker : IEvidenceRanker
{
    public IReadOnlyList<RankedEvidence> Rank(
        IReadOnlyCollection<EvidenceRecord> candidates,
        RetrievalSpecification specification,
        SupervisorExecutionState? state)
    {
        var referenceTime = specification.To ??
            candidates.Select(item => item.CreatedAt).DefaultIfEmpty(DateTimeOffset.UnixEpoch).Max();

        return candidates
            .Select(item => Score(item, specification, state, referenceTime))
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Evidence.CreatedAt)
            .ThenBy(item => item.Evidence.EvidenceId)
            .ToArray();
    }

    private static RankedEvidence Score(
        EvidenceRecord evidence,
        RetrievalSpecification specification,
        SupervisorExecutionState? state,
        DateTimeOffset referenceTime)
    {
        var factors = new List<RankingFactor>
        {
            new("incident", 100, "Exact incident boundary match.")
        };

        if (specification.Components.Contains(evidence.Component, StringComparer.OrdinalIgnoreCase))
        {
            factors.Add(new("component", 30, "Exact affected component match."));
        }

        var typeIndex = specification.EvidenceTypes.ToList().IndexOf(evidence.EvidenceType);
        if (typeIndex >= 0)
        {
            factors.Add(new(
                "profile-type",
                Math.Max(8, 24 - (typeIndex * 4)),
                "Evidence category is prioritized by the target Agent profile."));
        }

        if (OverlapsWindow(evidence, specification.From, specification.To))
        {
            factors.Add(new("time-window", 20, "Evidence overlaps the requested incident window."));
        }

        if (specification.SourceAgents.Contains(evidence.SourceAgent, StringComparer.Ordinal))
        {
            factors.Add(new("source-agent", 8, "Source Agent is relevant to the retrieval profile."));
        }

        var goalMatches = CountTermMatches(
            specification.Goal,
            $"{evidence.Summary} {evidence.DetailedContent}");
        if (goalMatches > 0)
        {
            factors.Add(new(
                "goal-terms",
                Math.Min(18, goalMatches * 3),
                "Evidence contains terms from the current investigation goal."));
        }

        var hypothesisMatches = state?.ActiveHypotheses.Sum(hypothesis =>
            CountTermMatches(hypothesis.Description, evidence.Summary)) ?? 0;
        if (hypothesisMatches > 0)
        {
            factors.Add(new(
                "hypothesis-terms",
                Math.Min(12, hypothesisMatches * 2),
                "Evidence relates to an active compact hypothesis."));
        }

        var age = referenceTime - evidence.CreatedAt;
        var recencyWeight = age <= TimeSpan.FromHours(1)
            ? 10
            : age <= TimeSpan.FromDays(1)
                ? 6
                : 2;
        factors.Add(new("recency", recencyWeight, "Recent evidence is preferred."));
        factors.Add(new(
            "importance",
            Math.Clamp(evidence.Importance, 1, 5) * 2,
            "Application-assigned evidence importance."));

        return new RankedEvidence(evidence, factors.Sum(factor => factor.Weight), factors);
    }

    private static bool OverlapsWindow(
        EvidenceRecord evidence,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        if (from is null && to is null)
        {
            return false;
        }

        return (from is null || evidence.ObservedTo is null || evidence.ObservedTo >= from) &&
            (to is null || evidence.ObservedFrom is null || evidence.ObservedFrom <= to);
    }

    private static int CountTermMatches(string source, string candidate)
    {
        var terms = source.Split(
                [' ', ',', '.', ':', ';', '-', '_'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => term.Length >= 4)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return terms.Count(term => candidate.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}

public interface IEvidenceDeduplicator
{
    IReadOnlyList<RankedEvidence> Deduplicate(
        IReadOnlyList<RankedEvidence> rankedEvidence,
        out int duplicatesRemoved);
}

public sealed class DeterministicEvidenceDeduplicator : IEvidenceDeduplicator
{
    public IReadOnlyList<RankedEvidence> Deduplicate(
        IReadOnlyList<RankedEvidence> rankedEvidence,
        out int duplicatesRemoved)
    {
        var evidenceIds = new HashSet<Guid>();
        var contentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selected = new List<RankedEvidence>();

        foreach (var item in rankedEvidence)
        {
            var key = string.Join(
                '|',
                item.Evidence.SourceAgent,
                item.Evidence.EvidenceType,
                item.Evidence.Component.Trim(),
                Normalize(item.Evidence.Summary));

            if (!evidenceIds.Add(item.Evidence.EvidenceId) || !contentKeys.Add(key))
            {
                continue;
            }

            selected.Add(item);
        }

        duplicatesRemoved = rankedEvidence.Count - selected.Count;
        return selected;
    }

    private static string Normalize(string value)
    {
        return string.Join(' ', value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToUpperInvariant();
    }
}

public interface ISemanticEvidenceSearch
{
    Task<IReadOnlyList<EvidenceRecord>> SearchAsync(
        RetrievalSpecification specification,
        IReadOnlyCollection<EvidenceRecord> deterministicCandidates,
        CancellationToken cancellationToken);
}

public sealed class DisabledSemanticEvidenceSearch : ISemanticEvidenceSearch
{
    public Task<IReadOnlyList<EvidenceRecord>> SearchAsync(
        RetrievalSpecification specification,
        IReadOnlyCollection<EvidenceRecord> deterministicCandidates,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<EvidenceRecord>>([]);
    }
}
