namespace ContextEngineering.Web.Models;

public enum EvidenceType
{
    Log,
    Deployment,
    Database,
    Telemetry,
    Application
}

public sealed record EvidenceReference(Guid EvidenceId, string SourceAgent, EvidenceType EvidenceType, string Summary);

public sealed record ActiveHypothesis(string Description, double Confidence, IReadOnlyList<Guid> SupportingEvidenceIds);

public sealed record RankingFactor(string Name, double Weight, string Explanation);

public sealed record RetrievalSpecification(Guid IncidentId, string TargetAgent, string Goal, IReadOnlyCollection<EvidenceType> EvidenceTypes, IReadOnlyCollection<string> SourceAgents, IReadOnlyCollection<string> Components, DateTimeOffset? From, DateTimeOffset? To, int MaximumItems, int ContextBudget, string RetrievalReason, IReadOnlyDictionary<EvidenceType, int>? PerCategoryLimits, TimeSpan? MaximumEvidenceAge);

public sealed record ContextEvidence(Guid EvidenceId, string SourceAgent, EvidenceType EvidenceType, string Component, DateTimeOffset? ObservedFrom, DateTimeOffset? ObservedTo, string Summary, string DetailedContent, string RetrievalReason, double RankingScore, IReadOnlyList<RankingFactor>? RankingFactors, bool MatchedDeterministicFilters, bool SemanticRelevanceContributed);

public sealed record ContextBudgetUsage(int MaximumItems, int MaximumCharacters, int SelectedItems, int ApproximateCharacters);

public sealed record RetrievalDiagnostics(int TotalIncidentEvidence, int DeterministicCandidates, int SemanticCandidates, int DuplicatesRemoved, int RankedCandidates, int SelectedEvidenceCount, int ApproximateSelectedCharacters, int ConfiguredMaximumItems, int ConfiguredMaximumCharacters, long DurationMilliseconds, int RetrievalIteration, IReadOnlyList<Guid> SelectedEvidenceIds);

public sealed record ContextBundle(Guid IncidentId, string TargetAgent, string CurrentGoal, IReadOnlyList<ContextEvidence> Evidence, string IncidentSummary, RetrievalSpecification? RetrievalSpecification, IReadOnlyList<ActiveHypothesis>? RelevantHypotheses, IReadOnlyDictionary<string, string>? Constraints, ContextBudgetUsage? Budget, RetrievalDiagnostics? Diagnostics)
{
    public string CurrentAgentTask => CurrentGoal;
}

public sealed record AdditionalContextRequirement(EvidenceType RequiredEvidenceType, string Component, DateTimeOffset? From, DateTimeOffset? To, string MissingInformation, string Reason, IReadOnlyCollection<string>? Keywords);

public sealed record SupervisorStateView(Guid IncidentId, string CurrentGoal, string Status, IReadOnlyList<string> CompletedInvestigations, IReadOnlyList<string> PendingInvestigations, IReadOnlyList<ActiveHypothesis> ActiveHypotheses, IReadOnlyList<EvidenceReference> EvidenceReferences, double Confidence, int ApproximateCharacters);

public sealed record EvidenceSummaryView(Guid EvidenceId, string SourceAgent, EvidenceType EvidenceType, string Component, DateTimeOffset? ObservedFrom, DateTimeOffset? ObservedTo, string Summary, int Importance, DateTimeOffset CreatedAt, int ApproximateCharacters, int InvestigationRound);

public sealed record InvestigationTimelineEvent(int Sequence, DateTimeOffset Timestamp, string EventType, string Message, string? Agent, int? RetrievalIteration);

public sealed record AgentExecutionView(Guid ExecutionId, string Agent, string Task, int RetrievalIteration, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, string Status, ContextBundle? ContextBundle, string? FindingSummary, Guid? NewEvidenceId, AdditionalContextRequirement? AdditionalContextRequired);

public sealed record FinalRecommendationView(string LikelyRootCause, string Recommendation, string Status, double Confidence, IReadOnlyList<Guid> SupportingEvidenceIds);

public sealed record InvestigationMetricsView(int StoredEvidenceRecords, int ApproximateStoredCharacters, int ApproximateSupervisorStateCharacters, int CurrentContextRecords, int ApproximateCurrentContextCharacters);

public sealed record InvestigationView(Guid IncidentId, string IncidentSummary, string Status, SupervisorStateView? SupervisorState, IReadOnlyList<InvestigationTimelineEvent> Timeline, IReadOnlyList<AgentExecutionView> AgentExecutions, IReadOnlyList<EvidenceSummaryView> Evidence, InvestigationMetricsView Metrics, FinalRecommendationView? FinalRecommendation, string? FailureMessage = null);
