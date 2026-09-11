namespace ClaimsReview.Contracts;
public interface IWorkflowRunMessage
{
    Guid WorkflowRunId { get; }
}
public sealed record ClaimSubmissionRequest(string PolicyNumber, string ClaimantName, string ClaimType,
                                            string Description, decimal ClaimedAmount, DateOnly IncidentDate,
                                            IReadOnlyList<string> SupportingEvidence);
public sealed record StartClaimWorkflowResponse(Guid WorkflowRunId, string Status);
public sealed record ClaimDecisionResponse(Guid WorkflowRunId, string Status, ClaimDecisionDto? Decision,
                                           WorkflowSummaryDto Workflow, Guid? ApprovalRequestId = null);
public sealed record ClaimDecisionDto(string Outcome, decimal ApprovedAmount, string Reason, string? Reviewer,
                                      DateTimeOffset DecidedAt);
public sealed record WorkflowSummaryDto(Guid WorkflowRunId, string Status, long? ExecutionDurationMs,
                                        long? HumanWaitDurationMs, long? TotalElapsedDurationMs, string? FailureStage,
                                        string? Error);
public sealed record ClaimWorkflowRunDto(Guid WorkflowRunId, string PolicyNumber, string ClaimantName, string ClaimType,
                                         decimal ClaimedAmount, string Status, DateTimeOffset StartedAt,
                                         DateTimeOffset? CompletedAt, string? Error, ClaimDecisionDto? Decision,
                                         Guid? ApprovalRequestId = null);
public sealed record WorkflowEventDto(long Sequence, Guid WorkflowRunId, string EventType, string Stage, string Status,
                                      string? Summary, DateTimeOffset OccurredAt);
public sealed record ClaimApprovalSummary(Guid ApprovalRequestId, Guid WorkflowRunId, string PolicyNumber,
                                          string ClaimantName, decimal ClaimedAmount, string RiskLevel, int RiskScore,
                                          string Reason, DateTimeOffset RequestedAt, DateTimeOffset ExpiresAt,
                                          string Status);
public sealed record ClaimApprovalDetails(Guid ApprovalRequestId, Guid WorkflowRunId, string Status,
                                          ValidatedClaim Claim, ClaimRiskAssessment RiskAssessment,
                                          string ApprovalReason, IReadOnlyList<string> TriggeredRules,
                                          IReadOnlyList<string> ValidationWarnings, string? Reviewer, string? Comment,
                                          DateTimeOffset RequestedAt, DateTimeOffset ExpiresAt,
                                          DateTimeOffset? DecidedAt);
public sealed record ApprovalDecisionRequest(string Reviewer, string? Comment);
public sealed record QueueResumeResponse(Guid WorkflowRunId, Guid ApprovalRequestId, string Status);
public sealed record ValidatedClaim(string PolicyNumber, string ClaimantName, string ClaimType, string IncidentSummary,
                                    decimal ClaimedAmount, DateOnly IncidentDate,
                                    IReadOnlyList<string> SupportingEvidence);
public enum ClaimRiskLevel
{
    Low,
    Medium,
    High
}
public sealed record ClaimRiskAssessment(ClaimRiskLevel RiskLevel, int RiskScore, IReadOnlyList<string> RiskFactors,
                                         string Recommendation, bool RequiresHumanReview);
public sealed record ClaimApprovalRequest(Guid ApprovalRequestId, Guid WorkflowRunId, ValidatedClaim Claim,
                                          ClaimRiskAssessment RiskAssessment, string ApprovalReason,
                                          IReadOnlyList<string> TriggeredRules, DateTimeOffset RequestedAt,
                                          DateTimeOffset ExpiresAt)
    : IWorkflowRunMessage;
public sealed record ClaimApprovalDecision(Guid ApprovalRequestId, Guid WorkflowRunId, ClaimApprovalOutcome Outcome,
                                           string Reviewer, string? Comment, DateTimeOffset DecidedAt)
    : IWorkflowRunMessage;
public enum ClaimApprovalOutcome
{
    Approved,
    Rejected
}
