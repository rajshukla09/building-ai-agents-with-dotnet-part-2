namespace EnterpriseArchitectureAssessment.Api.Contracts;
public class AssessmentRunResponse
{
    public Guid Id { get; set; }
    public string Objective { get; set; } = "";
    public string Status { get; set; } = "Queued";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public long? DurationMilliseconds { get; set; }
    public AssessmentConfigurationSnapshot Configuration { get; set; } = new();
    public AssessmentPlan? InitialPlan { get; set; }
    public AssessmentPlan? CurrentPlan { get; set; }
    public List<string> CompletedTasks { get; set; } = [];
    public List<string> OpenQuestions { get; set; } = [];
    public List<PlanRevision> PlanRevisions { get; set; } = [];
    public List<AssessmentConflict> Conflicts { get; set; } = [];
    public List<AssessmentFinding> Findings { get; set; } = [];
    public List<AgentInvocation> AgentInvocations { get; set; } = [];
    public List<ToolInvocation> ToolInvocations { get; set; } = [];
    public List<string> ProgressEvaluations { get; set; } = [];
    public List<AssessmentEvent> Events { get; set; } = [];
    public string? FinalRecommendation { get; set; }
    public string? CompletionReason { get; set; }
    public bool LimitReached { get; set; }
}
public sealed record AssessmentConfigurationSnapshot
{
    public string ModelDeployment { get; init; } = "";
    public int MaxTurns { get; init; }
    public int MaxAgentInvocations { get; init; }
    public int MaxToolCallsPerAgent { get; init; }
    public string InstructionVersion { get; init; } = "chapter-04-v1";
}
