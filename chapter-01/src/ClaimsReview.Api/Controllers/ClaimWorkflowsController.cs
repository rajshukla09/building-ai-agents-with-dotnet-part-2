using System.Text.Json;
using ClaimsReview.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClaimsReview.Api.Controllers;

[ApiController]
[Route("api/claim-workflows")]
public sealed class ClaimWorkflowsController(
    IClaimWorkflowService workflowService,
    ClaimsDbContext db) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<StartClaimWorkflowResponse>> Start(
        ClaimSubmissionRequest request,
        CancellationToken cancellationToken)
    {
        var response = await workflowService.StartAsync(request, cancellationToken);
        return AcceptedAtAction(nameof(Get), new { id = response.WorkflowRunId }, response);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ClaimWorkflowRunDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var run = await db.ClaimWorkflowRuns.FindAsync([id], cancellationToken);
        if (run is null)
        {
            return NotFound();
        }

        var decision = run.FinalDecisionJson is null
            ? null
            : JsonSerializer.Deserialize<ClaimDecisionDto>(
                run.FinalDecisionJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        // SQLite cannot translate ORDER BY for DateTimeOffset. Materialize the small
        // set of approvals for this workflow, then select the newest one in memory.
        var approvals = await db.ClaimApprovals
            .Where(approval => approval.WorkflowRunId == id)
            .Select(approval => new
            {
                approval.ApprovalRequestId,
                approval.RequestedAt,
            })
            .ToListAsync(cancellationToken);
        var approvalRequestId = approvals
            .OrderByDescending(approval => approval.RequestedAt)
            .Select(approval => (Guid?)approval.ApprovalRequestId)
            .FirstOrDefault();

        return Ok(new ClaimWorkflowRunDto(
            run.WorkflowRunId,
            run.PolicyNumber,
            run.ClaimantName,
            run.ClaimType,
            run.ClaimedAmount,
            run.Status,
            run.StartedAt,
            run.CompletedAt,
            run.Error,
            decision,
            approvalRequestId));
    }

    [HttpGet("{id:guid}/events")]
    public async Task<ActionResult<IReadOnlyList<WorkflowEventDto>>> GetEvents(
        Guid id,
        [FromQuery] long? afterSequence,
        CancellationToken cancellationToken)
    {
        var events = await db.ClaimWorkflowEvents
            .Where(e => e.WorkflowRunId == id && e.Sequence > (afterSequence ?? 0))
            .OrderBy(e => e.Sequence)
            .Select(e => new WorkflowEventDto(
                e.Sequence,
                e.WorkflowRunId,
                e.EventType,
                e.Stage,
                e.Status,
                e.Summary,
                e.OccurredAt))
            .ToListAsync(cancellationToken);

        return Ok(events);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        return await workflowService.CancelAsync(id, cancellationToken)
            ? Accepted()
            : NotFound();
    }
}
