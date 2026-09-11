namespace EnterpriseArchitectureAssessment.Api.Contracts;

public sealed record AssessmentRunMetrics(
    Guid Id, string Objective, string Status, DateTimeOffset StartedAt, long? DurationMilliseconds,
    int AgentInvocations, IReadOnlyList<string> UniqueAgents, int McpCalls,
    IReadOnlyList<string> UniqueMcpTools, IReadOnlyDictionary<string, int> CallsPerAgent,
    int PlanRevisions, int FailedToolCalls, int CacheHits, bool LimitReached,
    int RecommendationLength, AssessmentPlan? InitialPlan, AssessmentPlan? CurrentPlan,
    string? CompletionReason, string? FinalRecommendation, AssessmentConfigurationSnapshot Configuration);

public sealed record AssessmentRunComparison(AssessmentRunMetrics Left, AssessmentRunMetrics Right);
