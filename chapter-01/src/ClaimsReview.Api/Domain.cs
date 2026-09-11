using ClaimsReview.Contracts;
using System.ComponentModel.DataAnnotations;
namespace ClaimsReview.Api;

public sealed class AzureOpenAIOptions
{
    public const string SectionName = "AzureOpenAI";

    [Required(AllowEmptyStrings = false)]
    [Url]
    public string Endpoint { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string ApiKey { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string DeploymentName { get; init; } = string.Empty;
}
public sealed record ClaimDraft(string? PolicyNumber, string? ClaimantName, string? ClaimType, string? IncidentSummary,
                                decimal? ClaimedAmount, DateOnly? IncidentDate,
                                IReadOnlyList<string>? SupportingEvidence);
public sealed record ClaimIntakeAgentRequest(ClaimSubmissionRequest Submission);
public sealed record RiskAssessmentAgentRequest(ValidatedClaim Claim, IReadOnlyList<string> ValidationWarnings);
public sealed record ClaimRiskAssessmentDraft(string? RiskLevel, int? RiskScore, IReadOnlyList<string>? RiskFactors,
                                              string? Recommendation, bool? RequiresHumanReview);
public enum AgentFailureKind
{
    StructuredOutput,
    Validation,
    Refusal,
    Timeout,
    RateLimit,
    Dependency,
    Policy,
    Infrastructure
}
public sealed record AgentFailure(AgentFailureKind Kind, string Code, string Message, string? Path = null);
public sealed record AgentExecutionMetadata(string AgentName, int AttemptCount, long DurationMs,
                                            IReadOnlyList<string> Warnings);
public sealed record AgentResult<T>(bool IsSuccess, T? Value, AgentFailure? Failure, AgentExecutionMetadata Metadata)
{
    public static AgentResult<T> Success(T v, string n, long ms = 0) => new(true, v, null, new(n, 1, ms, []));
    public static AgentResult<T> Fail(string n, AgentFailure f) => new(false, default, f, new(n, 1, 0, []));
}
public interface IApplicationAgent<in TRequest, TResponse>
{
    Task<AgentResult<TResponse>> ExecuteAsync(TRequest request, CancellationToken cancellationToken = default);
}
public interface IClaimIntakeAgent : IApplicationAgent<ClaimIntakeAgentRequest, ClaimDraft>
{
}
public interface IRiskAssessmentAgent : IApplicationAgent<RiskAssessmentAgentRequest, ClaimRiskAssessment>
{
}
public sealed record ClaimValidationError(string Field, string Message);
public sealed record ClaimValidationResult(bool IsValid, IReadOnlyList<ClaimValidationError> Errors,
                                           IReadOnlyList<string> Warnings);
public interface IClaimValidator
{
    ClaimValidationResult Validate(ClaimDraft draft, DateOnly today);
    ValidatedClaim ToValidatedClaim(ClaimDraft draft);
}
public sealed record HumanApprovalRequirement(bool IsRequired, string Reason, IReadOnlyList<string> TriggeredRules);
public interface IHumanApprovalPolicy
{
    HumanApprovalRequirement Evaluate(ValidatedClaim claim, ClaimRiskAssessment riskAssessment,
                                      IReadOnlyList<string>? warnings = null);
}
public sealed class HumanApprovalOptions
{
    public bool Enabled { get; set; } = true;
    public decimal AmountThreshold { get; set; } = 50000;
    public int HighRiskScoreThreshold { get; set; } = 70;
    public int ApprovalTimeoutMinutes { get; set; } = 30;
}
public sealed record ClaimDraftMessage(Guid WorkflowRunId, ClaimSubmissionRequest OriginalRequest, ClaimDraft Draft)
    : IWorkflowRunMessage;
public sealed record ValidatedClaimMessage(Guid WorkflowRunId, ClaimSubmissionRequest OriginalRequest,
                                           ValidatedClaim Claim, IReadOnlyList<string> ValidationWarnings)
    : IWorkflowRunMessage;
public sealed record RiskAssessmentMessage(Guid WorkflowRunId, ClaimSubmissionRequest OriginalRequest,
                                           ValidatedClaim Claim, ClaimRiskAssessment RiskAssessment,
                                           IReadOnlyList<string> ValidationWarnings,
                                           HumanApprovalRequirement ApprovalRequirement)
    : IWorkflowRunMessage;
public sealed record NativeApprovalResponseCommand(Guid WorkflowRunId, Guid ApprovalRequestId,
                                                   ClaimApprovalDecision Decision);
