namespace EnterpriseArchitectureAssessment.Api.Contracts;
public sealed record AssessmentPlanItem(string Task, string? DelegatedAgent = null, bool Completed = false);
public sealed record AssessmentPlan(int Version, IReadOnlyList<AssessmentPlanItem> Items, string Rationale, DateTimeOffset At);
