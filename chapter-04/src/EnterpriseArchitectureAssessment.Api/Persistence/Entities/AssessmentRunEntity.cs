namespace EnterpriseArchitectureAssessment.Api.Persistence.Entities;

public sealed class AssessmentRunEntity
{
    public Guid Id { get; set; }
    public string Objective { get; set; } = "";
    public string Status { get; set; } = "Queued";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public long? DurationMilliseconds { get; set; }
    public string? InitialPlanJson { get; set; }
    public string? CurrentPlanJson { get; set; }
    public string? AdditionalObservabilityJson { get; set; }
    public string? CompletionReason { get; set; }
    public string? FinalRecommendation { get; set; }
    public bool LimitReached { get; set; }
    public string ModelDeployment { get; set; } = "";
    public int MaxTurns { get; set; }
    public int MaxAgentInvocations { get; set; }
    public int MaxToolCallsPerAgent { get; set; }
    public string InstructionVersion { get; set; } = "chapter-04-v1";
    public List<PlanRevisionEntity> PlanRevisions { get; set; } = [];
    public List<AgentInvocationEntity> AgentInvocations { get; set; } = [];
    public List<ToolInvocationEntity> ToolInvocations { get; set; } = [];
    public List<AssessmentEventEntity> Events { get; set; } = [];
    public List<ProgressEvaluationEntity> ProgressEvaluations { get; set; } = [];
}
