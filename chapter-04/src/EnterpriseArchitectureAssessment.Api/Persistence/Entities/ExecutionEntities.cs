namespace EnterpriseArchitectureAssessment.Api.Persistence.Entities;

public sealed class PlanRevisionEntity
{
    public long Id { get; set; }
    public Guid AssessmentRunId { get; set; }
    public int FromVersion { get; set; }
    public int ToVersion { get; set; }
    public string Evidence { get; set; } = "";
    public string Change { get; set; } = "";
    public DateTimeOffset At { get; set; }
}

public sealed class AgentInvocationEntity
{
    public Guid Id { get; set; }
    public Guid AssessmentRunId { get; set; }
    public string Agent { get; set; } = "";
    public string Task { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Result { get; set; }
}

public sealed class ToolInvocationEntity
{
    public long Id { get; set; }
    public Guid AssessmentRunId { get; set; }
    public Guid AgentInvocationId { get; set; }
    public string? Agent { get; set; }
    public string Server { get; set; } = "";
    public string Tool { get; set; } = "";
    public string NormalizedArguments { get; set; } = "";
    public bool CacheHit { get; set; }
    public DateTimeOffset At { get; set; }
    public long? DurationMilliseconds { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}

public sealed class AssessmentEventEntity
{
    public long Id { get; set; }
    public Guid AssessmentRunId { get; set; }
    public long Sequence { get; set; }
    public string Type { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public string Summary { get; set; } = "";
    public string? Agent { get; set; }
    public string? Tool { get; set; }
    public string? PlanJson { get; set; }
}

public sealed class ProgressEvaluationEntity
{
    public long Id { get; set; }
    public Guid AssessmentRunId { get; set; }
    public int Sequence { get; set; }
    public string Text { get; set; } = "";
}
