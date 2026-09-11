namespace ContextEngineering.Api.Models;

public sealed record ContextRequest(
    Guid IncidentId,
    string TargetAgent,
    string CurrentGoal,
    IReadOnlyCollection<EvidenceType>? AllowedEvidenceTypes = null,
    string? Component = null,
    int MaximumItems = 20,
    SupervisorExecutionState? ExecutionState = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    IReadOnlyCollection<string>? SourceAgents = null,
    int RetrievalIteration = 1);

public sealed record RetrievalSpecification(
    Guid IncidentId,
    string TargetAgent,
    string Goal,
    IReadOnlyCollection<EvidenceType> EvidenceTypes,
    IReadOnlyCollection<string> SourceAgents,
    IReadOnlyCollection<string> Components,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int MaximumItems,
    int ContextBudget,
    string RetrievalReason,
    IReadOnlyDictionary<EvidenceType, int>? PerCategoryLimits = null,
    TimeSpan? MaximumEvidenceAge = null);

public sealed record RankingFactor(string Name, double Weight, string Explanation);

public sealed record ContextEvidence(
    Guid EvidenceId,
    string SourceAgent,
    EvidenceType EvidenceType,
    string Component,
    DateTimeOffset? ObservedFrom,
    DateTimeOffset? ObservedTo,
    string Summary,
    string DetailedContent,
    string RetrievalReason = "Deterministic incident evidence selection",
    double RankingScore = 0,
    IReadOnlyList<RankingFactor>? RankingFactors = null,
    bool MatchedDeterministicFilters = true,
    bool SemanticRelevanceContributed = false);

public sealed record ContextBudgetUsage(
    int MaximumItems,
    int MaximumCharacters,
    int SelectedItems,
    int ApproximateCharacters);

public sealed record RetrievalDiagnostics(
    int TotalIncidentEvidence,
    int DeterministicCandidates,
    int SemanticCandidates,
    int DuplicatesRemoved,
    int RankedCandidates,
    int SelectedEvidenceCount,
    int ApproximateSelectedCharacters,
    int ConfiguredMaximumItems,
    int ConfiguredMaximumCharacters,
    long DurationMilliseconds,
    int RetrievalIteration,
    IReadOnlyList<Guid> SelectedEvidenceIds);

public sealed record ContextBundle(
    Guid IncidentId,
    string TargetAgent,
    string CurrentGoal,
    IReadOnlyList<ContextEvidence> Evidence,
    string IncidentSummary = "",
    RetrievalSpecification? RetrievalSpecification = null,
    IReadOnlyList<ActiveHypothesis>? RelevantHypotheses = null,
    IReadOnlyDictionary<string, string>? Constraints = null,
    ContextBudgetUsage? Budget = null,
    RetrievalDiagnostics? Diagnostics = null)
{
    public string CurrentAgentTask => CurrentGoal;

    public IReadOnlyList<Guid> EvidenceIds => Evidence
        .Select(item => item.EvidenceId)
        .ToArray();
}
