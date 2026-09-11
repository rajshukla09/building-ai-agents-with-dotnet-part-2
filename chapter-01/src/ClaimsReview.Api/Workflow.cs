using System.Text.Json;
using ClaimsReview.Contracts;
using Microsoft.Agents.AI.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClaimsReview.Api;

public abstract class ClaimExecutor<TIn, TOut>(string name, ClaimsDbContext db, TimeProvider time)
    : Executor<TIn, TOut>(name)
    where TIn : IWorkflowRunMessage
{
    public override async ValueTask<TOut> HandleAsync(
        TIn message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var startedAt = time.GetUtcNow();
        await Event(db, message.WorkflowRunId, $"{name}Started", "Executor", "Running",
            $"{name} started", time, cancellationToken);
        try
        {
            var output = await ExecuteAsync(message, cancellationToken);
            db.ClaimExecutorTraces.Add(new ClaimExecutorTraceRecord
            {
                WorkflowRunId = message.WorkflowRunId,
                ExecutorName = name,
                Status = "Completed",
                DurationMs = (long)(time.GetUtcNow() - startedAt).TotalMilliseconds,
            });
            await db.SaveChangesAsync(cancellationToken);
            await Event(db, message.WorkflowRunId, $"{name}Completed", "Executor", "Completed",
                $"{name} produced {typeof(TOut).Name}", time, cancellationToken);
            return output;
        }
        catch (Exception exception)
        {
            db.ClaimExecutorTraces.Add(new ClaimExecutorTraceRecord
            {
                WorkflowRunId = message.WorkflowRunId,
                ExecutorName = name,
                Status = exception is OperationCanceledException ? "Cancelled" : "Failed",
                DurationMs = (long)(time.GetUtcNow() - startedAt).TotalMilliseconds,
                Error = exception.Message,
            });
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    protected abstract ValueTask<TOut> ExecuteAsync(TIn message, CancellationToken cancellationToken);

    public static async Task Event(
        ClaimsDbContext db, Guid id, string type, string stage, string status, string summary,
        TimeProvider time, CancellationToken cancellationToken, string? dataJson = null)
    {
        await WorkflowEventSequenceLock.ExecuteAsync(id, async () =>
        {
            var sequence = await db.ClaimWorkflowEvents
                .Where(item => item.WorkflowRunId == id)
                .Select(item => (long?)item.Sequence)
                .MaxAsync(cancellationToken) ?? 0;
            var record = new ClaimWorkflowEventRecord
            {
                WorkflowRunId = id,
                Sequence = sequence + 1,
                EventType = type,
                Stage = stage,
                Status = status,
                Summary = summary,
                DataJson = dataJson,
                OccurredAt = time.GetUtcNow(),
            };
            db.ClaimWorkflowEvents.Add(record);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (
                exception.InnerException?.Message.Contains(
                    "ClaimWorkflowEvents.WorkflowRunId, ClaimWorkflowEvents.Sequence",
                    StringComparison.Ordinal) == true)
            {
                db.Entry(record).State = EntityState.Detached;
                record.Sequence = (await db.ClaimWorkflowEvents
                    .Where(item => item.WorkflowRunId == id)
                    .Select(item => (long?)item.Sequence)
                    .MaxAsync(cancellationToken) ?? 0) + 1;
                db.ClaimWorkflowEvents.Add(record);
                await db.SaveChangesAsync(cancellationToken);
            }
        }, cancellationToken);
    }
}

public static class WorkflowEventSequenceLock
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, SemaphoreSlim>
        Locks = new();

    public static async Task ExecuteAsync(
        Guid workflowRunId,
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        var gate = Locks.GetOrAdd(workflowRunId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            await action();
        }
        finally
        {
            gate.Release();
        }
    }
}

public sealed record WorkflowStartMessage(Guid WorkflowRunId, ClaimSubmissionRequest Request)
    : IWorkflowRunMessage;

public sealed class ClaimIntakeAgentExecutor(
    IClaimIntakeAgent agent, ClaimsDbContext db, TimeProvider time)
    : ClaimExecutor<WorkflowStartMessage, ClaimDraftMessage>(nameof(ClaimIntakeAgentExecutor), db, time)
{
    protected override async ValueTask<ClaimDraftMessage> ExecuteAsync(
        WorkflowStartMessage message, CancellationToken cancellationToken)
    {
        var result = await agent.ExecuteAsync(new ClaimIntakeAgentRequest(message.Request), cancellationToken);
        if (!result.IsSuccess) throw new InvalidOperationException(result.Failure!.Message);
        return new ClaimDraftMessage(message.WorkflowRunId, message.Request, result.Value!);
    }
}

public sealed class ClaimValidationExecutor(IClaimValidator validator, ClaimsDbContext db, TimeProvider time)
    : ClaimExecutor<ClaimDraftMessage, ValidatedClaimMessage>(nameof(ClaimValidationExecutor), db, time)
{
    protected override ValueTask<ValidatedClaimMessage> ExecuteAsync(
        ClaimDraftMessage message, CancellationToken cancellationToken)
    {
        var result = validator.Validate(message.Draft, DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime));
        if (!result.IsValid) throw new ClaimValidationException(result);
        return ValueTask.FromResult(new ValidatedClaimMessage(message.WorkflowRunId,
            message.OriginalRequest, validator.ToValidatedClaim(message.Draft), result.Warnings));
    }
}

public sealed class ClaimValidationException(ClaimValidationResult result)
    : Exception(string.Join("; ", result.Errors.Select(error => $"{error.Field}: {error.Message}")))
{
    public ClaimValidationResult Result { get; } = result;
}

public sealed class RiskAssessmentAgentExecutor(
    IRiskAssessmentAgent agent,
    IHumanApprovalPolicy approvalPolicy,
    ClaimsDbContext db,
    TimeProvider time)
    : ClaimExecutor<ValidatedClaimMessage, RiskAssessmentMessage>(nameof(RiskAssessmentAgentExecutor), db, time)
{
    protected override async ValueTask<RiskAssessmentMessage> ExecuteAsync(
        ValidatedClaimMessage message, CancellationToken cancellationToken)
    {
        var result = await agent.ExecuteAsync(
            new RiskAssessmentAgentRequest(message.Claim, message.ValidationWarnings), cancellationToken);
        if (!result.IsSuccess) throw new InvalidOperationException(result.Failure!.Message);
        await Event(db, message.WorkflowRunId, "RiskAssessmentCompleted", "Risk Assessment",
            "Completed", "Risk assessment completed.", time, cancellationToken);
        var approvalRequirement = approvalPolicy.Evaluate(
            message.Claim, result.Value!, message.ValidationWarnings);
        return new RiskAssessmentMessage(message.WorkflowRunId, message.OriginalRequest,
            message.Claim, result.Value!, message.ValidationWarnings, approvalRequirement);
    }
}

public sealed class HumanApprovalRequestExecutor(
    IOptions<HumanApprovalOptions> options,
    ClaimsDbContext db,
    TimeProvider time)
    : ClaimExecutor<RiskAssessmentMessage, ClaimApprovalRequest>(nameof(HumanApprovalRequestExecutor), db, time)
{
    protected override ValueTask<ClaimApprovalRequest> ExecuteAsync(
        RiskAssessmentMessage message, CancellationToken cancellationToken)
    {
        var requirement = message.ApprovalRequirement;
        if (!requirement.IsRequired)
        {
            throw new InvalidOperationException(
                "A claim that does not require human review cannot enter the approval path.");
        }
        var now = time.GetUtcNow();
        return ValueTask.FromResult(new ClaimApprovalRequest(
            Guid.NewGuid(), message.WorkflowRunId, message.Claim, message.RiskAssessment,
            requirement.Reason, requirement.TriggeredRules, now,
            now.AddMinutes(options.Value.ApprovalTimeoutMinutes)));
    }
}

public sealed class ClaimDecisionExecutor(ClaimsDbContext db, TimeProvider time)
    : ClaimExecutor<IWorkflowRunMessage, ClaimDecisionResponse>(nameof(ClaimDecisionExecutor), db, time)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async ValueTask<ClaimDecisionResponse> ExecuteAsync(
        IWorkflowRunMessage message, CancellationToken cancellationToken)
    {
        if (message is RiskAssessmentMessage riskMessage)
        {
            if (riskMessage.ApprovalRequirement.IsRequired)
            {
                throw new InvalidOperationException(
                    "A claim requiring human review cannot bypass the approval path.");
            }

            var decidedAt = time.GetUtcNow();
            var automaticDecision = new ClaimDecisionDto(
                "Approved",
                riskMessage.Claim.ClaimedAmount,
                riskMessage.ApprovalRequirement.Reason,
                null,
                decidedAt);
            var automaticSummary = new WorkflowSummaryDto(riskMessage.WorkflowRunId,
                "Completed", null, null, null, null, null);
            return new ClaimDecisionResponse(
                riskMessage.WorkflowRunId, automaticSummary.Status, automaticDecision, automaticSummary);
        }

        if (message is not ClaimApprovalDecision approvalDecision)
        {
            throw new InvalidOperationException(
                $"Unsupported claim decision input {message.GetType().Name}.");
        }

        if (approvalDecision.ApprovalRequestId == Guid.Empty || approvalDecision.WorkflowRunId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "The approval decision is missing its approval or workflow identifier.");
        }

        var approval = await db.ClaimApprovals
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.ApprovalRequestId == approvalDecision.ApprovalRequestId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"Approval {approvalDecision.ApprovalRequestId} was not found for claim decision.");
        if (approval.WorkflowRunId != approvalDecision.WorkflowRunId)
        {
            throw new InvalidOperationException(
                $"Approval {approvalDecision.ApprovalRequestId} does not belong to workflow {approvalDecision.WorkflowRunId}.");
        }

        var claim = JsonSerializer.Deserialize<ValidatedClaim>(
            approval.ClaimSnapshotJson,
            JsonOptions) ?? throw new InvalidOperationException(
                $"Approval {approvalDecision.ApprovalRequestId} has no valid claim snapshot.");
        var approved = approvalDecision.Outcome == ClaimApprovalOutcome.Approved;
        var decision = new ClaimDecisionDto(
            approved ? "Approved" : "Rejected",
            approved ? claim.ClaimedAmount : 0,
            approved ? "Claim approved by the human reviewer." : "Claim rejected by the human reviewer.",
            approvalDecision.Reviewer,
            approvalDecision.DecidedAt);
        var summary = new WorkflowSummaryDto(approvalDecision.WorkflowRunId,
            approved ? "Completed" : "Rejected", null, null, null, null, null);
        return new ClaimDecisionResponse(
            approvalDecision.WorkflowRunId, summary.Status, decision, summary, approvalDecision.ApprovalRequestId);
    }
}

public sealed class ClaimReviewWorkflow(
    ClaimIntakeAgentExecutor intake,
    ClaimValidationExecutor validation,
    RiskAssessmentAgentExecutor risk,
    HumanApprovalRequestExecutor approvalRequest,
    ClaimDecisionExecutor decision)
{
    public Workflow Create()
    {
        var approvalPort = RequestPort.Create<ClaimApprovalRequest, ClaimApprovalDecision>(
            "claim-human-approval");
        return new WorkflowBuilder(intake)
            .AddEdge(intake, validation)
            .AddEdge(validation, risk)
            .AddEdge<RiskAssessmentMessage>(
                risk,
                approvalRequest,
                message => message!.ApprovalRequirement.IsRequired,
                "Human approval required")
            .AddEdge<RiskAssessmentMessage>(
                risk,
                decision,
                message => !message!.ApprovalRequirement.IsRequired,
                "Human approval not required")
            .AddEdge(approvalRequest, approvalPort)
            .AddEdge(approvalPort, decision)
            .WithOutputFrom(decision)
            .Build();
    }

    public Workflow CreateDecisionContinuation() =>
        new WorkflowBuilder(decision)
            .WithOutputFrom(decision)
            .Build();
}
