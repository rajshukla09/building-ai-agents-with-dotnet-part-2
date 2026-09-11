namespace EnterpriseArchitectureAssessment.Api.Contracts;
public sealed record PlanRevision(int FromVersion, int ToVersion, string Evidence, string Change, DateTimeOffset At);
