using System.Text.Json;
using ClaimsReview.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ClaimsReview.Api.Controllers;

[ApiController]
[Route("api/claim-approvals")]
public sealed class ClaimApprovalsController(
    IClaimApprovalStore approvalStore,
    IClaimWorkflowQueue workflowQueue) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ClaimApprovalSummary>>> GetPending(
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        return Ok(await approvalStore.GetAllAsync(status, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ClaimApprovalDetails>> Get(Guid id, CancellationToken cancellationToken)
    {
        var approval = await approvalStore.GetAsync(id, cancellationToken);
        if (approval is null)
        {
            return NotFound();
        }

        return Ok(new ClaimApprovalDetails(
            approval.ApprovalRequestId,
            approval.WorkflowRunId,
            approval.Status,
            JsonSerializer.Deserialize<ValidatedClaim>(approval.ClaimSnapshotJson, JsonOptions)!,
            JsonSerializer.Deserialize<ClaimRiskAssessment>(approval.RiskAssessmentJson, JsonOptions)!,
            approval.Reason,
            JsonSerializer.Deserialize<IReadOnlyList<string>>(approval.TriggeredRulesJson, JsonOptions)!,
            JsonSerializer.Deserialize<IReadOnlyList<string>>(approval.ValidationWarningsJson, JsonOptions)!,
            approval.Reviewer,
            approval.Comment,
            approval.RequestedAt,
            approval.ExpiresAt,
            approval.DecidedAt));
    }

    [HttpPost("{id:guid}/approve")]
    public Task<IActionResult> Approve(
        Guid id,
        ApprovalDecisionRequest request,
        CancellationToken cancellationToken) =>
        Decide(id, ClaimApprovalOutcome.Approved, request, cancellationToken);

    [HttpPost("{id:guid}/reject")]
    public Task<IActionResult> Reject(
        Guid id,
        ApprovalDecisionRequest request,
        CancellationToken cancellationToken) =>
        Decide(id, ClaimApprovalOutcome.Rejected, request, cancellationToken);

    private async Task<IActionResult> Decide(
        Guid id,
        ClaimApprovalOutcome outcome,
        ApprovalDecisionRequest request,
        CancellationToken cancellationToken)
    {
        var approval = await approvalStore.GetAsync(id, cancellationToken);
        if (approval is null)
        {
            return NotFound();
        }

        var decision = new ClaimApprovalDecision(
            id,
            approval.WorkflowRunId,
            outcome,
            request.Reviewer,
            request.Comment,
            DateTimeOffset.UtcNow);
        var result = await approvalStore.DecideAsync(decision, cancellationToken);

        if (result.Status == ApprovalDecisionStatus.Expired)
        {
            return StatusCode(StatusCodes.Status410Gone);
        }

        if (result.Status == ApprovalDecisionStatus.Conflict)
        {
            return Conflict();
        }

        if (result.Status == ApprovalDecisionStatus.NotFound)
        {
            return NotFound();
        }

        if (result.Status == ApprovalDecisionStatus.Accepted)
        {
            await workflowQueue.EnqueueResumeAsync(
                new NativeApprovalResponseCommand(
                    approval.WorkflowRunId,
                    id,
                    decision),
                cancellationToken);

            return Accepted(new QueueResumeResponse(
                approval.WorkflowRunId,
                id,
                "ResumeQueued"));
        }

        // Requeueing an identical decision supports recovery if the in-memory queue
        // was lost during an application restart. ResumeAsync validates that the run
        // is still waiting, so a duplicate command cannot execute the continuation twice.
        await workflowQueue.EnqueueResumeAsync(
            new NativeApprovalResponseCommand(approval.WorkflowRunId, id, decision),
            cancellationToken);
        return Ok(new QueueResumeResponse(approval.WorkflowRunId, id, "ResumeQueued"));
    }

}
