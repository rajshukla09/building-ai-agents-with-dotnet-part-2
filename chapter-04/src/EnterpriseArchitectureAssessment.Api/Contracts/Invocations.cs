namespace EnterpriseArchitectureAssessment.Api.Contracts;
public sealed record AgentInvocation(Guid Id, string Agent, string Task, DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt = null, string? Result = null);
public sealed record ToolInvocation(Guid AgentInvocationId, string Server, string Tool, string NormalizedArguments,
    bool CacheHit, DateTimeOffset At, long? DurationMilliseconds = null, bool Success = true,
    string? Error = null, string? Agent = null);
