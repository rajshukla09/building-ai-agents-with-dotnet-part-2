namespace EnterpriseArchitectureAssessment.Api.Contracts;
public sealed record AssessmentEvent(long Sequence, string Type, DateTimeOffset At, string Summary,
    string? Agent = null, string? Tool = null, string? PlanJson = null);
public static class AssessmentEventTypes
{
    public const string AssessmentStarted="AssessmentStarted", PlanCreated="PlanCreated", TaskDelegated="TaskDelegated",
        AgentStarted="AgentStarted", McpToolCalled="McpToolCalled", AgentCompleted="AgentCompleted",
        ProgressEvaluated="ProgressEvaluated", PlanRevised="PlanRevised", ConflictDetected="ConflictDetected",
        CompletionDeclared="CompletionDeclared", AssessmentCompleted="AssessmentCompleted", AssessmentFailed="AssessmentFailed";
}
