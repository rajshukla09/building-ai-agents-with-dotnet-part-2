using System.Text.Json;
using System.Threading.Channels;
using ClaimsReview.Contracts;
using Microsoft.Agents.AI.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClaimsReview.Api;

public interface IClaimWorkflowService
{
    Task<StartClaimWorkflowResponse> StartAsync(ClaimSubmissionRequest request, CancellationToken cancellationToken);
    Task<ClaimDecisionResponse?> ResumeAsync(NativeApprovalResponseCommand command, CancellationToken cancellationToken);
    Task<bool> CancelAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class ClaimWorkflowService(
    ClaimReviewWorkflow factory,
    ClaimsDbContext db,
    TimeProvider time,
    IClaimWorkflowQueue workflowQueue,
    IOptions<HumanApprovalOptions> approvalOptions,
    ILogger<ClaimWorkflowService> logger) : IClaimWorkflowService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<StartClaimWorkflowResponse> StartAsync(
        ClaimSubmissionRequest request,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        db.ClaimWorkflowRuns.Add(new ClaimWorkflowRunRecord
        {
            WorkflowRunId = id,
            PolicyNumber = request.PolicyNumber,
            ClaimantName = request.ClaimantName,
            ClaimType = request.ClaimType,
            ClaimedAmount = request.ClaimedAmount,
            Status = "Queued",
            StartedAt = time.GetUtcNow(),
            CreatedAt = time.GetUtcNow(),
            OriginalRequestJson = JsonSerializer.Serialize(request, JsonOptions),
        });
        await db.SaveChangesAsync(cancellationToken);

        await workflowQueue.EnqueueStartAsync(id, request, cancellationToken);

        return new StartClaimWorkflowResponse(id, "Queued");
    }

    public async Task<ClaimDecisionResponse?> ResumeAsync(
        NativeApprovalResponseCommand command,
        CancellationToken cancellationToken)
    {
        var run = await db.ClaimWorkflowRuns.FindAsync([command.WorkflowRunId], cancellationToken);
        var approval = await db.ClaimApprovals.FindAsync([command.ApprovalRequestId], cancellationToken);
        if (run is null
            || approval is null
            || approval.WorkflowRunId != command.WorkflowRunId
            || command.Decision.WorkflowRunId != command.WorkflowRunId
            || command.Decision.ApprovalRequestId != command.ApprovalRequestId
            || run.Status != "WaitingForApproval"
            || !string.Equals(
                approval.Status,
                command.Decision.Outcome.ToString(),
                StringComparison.Ordinal))
        {
            return null;
        }

        await ClaimExecutor<WorkflowStartMessage, ClaimDraftMessage>.Event(
            db, command.WorkflowRunId, "ApprovalDecisionReceived", "Human Approval",
            approval.Status, $"Approval decision received from {command.Decision.Reviewer}.",
            time, cancellationToken);

        run.Status = "Running";
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Restoring persisted decision continuation for workflow {WorkflowRunId}.",
            command.WorkflowRunId);
        await ClaimExecutor<WorkflowStartMessage, ClaimDraftMessage>.Event(
            db, command.WorkflowRunId, "WorkflowResumed", "Workflow", "Running",
            "Persisted workflow continuation resumed.", time, cancellationToken);

        var continuation = await InProcessExecution.RunAsync(
            factory.CreateDecisionContinuation(),
            command.Decision,
            cancellationToken: cancellationToken);
        var response = continuation.NewEvents
            .OfType<WorkflowOutputEvent>()
            .Select(item => item.Data)
            .OfType<ClaimDecisionResponse>()
            .LastOrDefault() ?? throw new InvalidOperationException(
                "The decision continuation completed without a ClaimDecisionResponse.");

        Complete(run, response, response.Status);
        run.HumanWaitDurationMs =
            (long)(time.GetUtcNow() - approval.RequestedAt).TotalMilliseconds;
        await ClaimExecutor<WorkflowStartMessage, ClaimDraftMessage>.Event(
            db, command.WorkflowRunId, "WorkflowCompleted", "Workflow", response.Status,
            $"Workflow completed with status {response.Status}.", time, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return response;
    }

    public async Task<bool> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        var run = await db.ClaimWorkflowRuns.FindAsync([id], cancellationToken);
        if (run is null)
        {
            return false;
        }

        run.Status = "Cancelled";
        run.CompletedAt = time.GetUtcNow();
        foreach (var approval in db.ClaimApprovals.Where(
                     approval => approval.WorkflowRunId == id && approval.Status == "Pending"))
        {
            approval.Status = "Cancelled";
        }

        await ClaimExecutor<WorkflowStartMessage, ClaimDraftMessage>.Event(
            db,
            id,
            "WorkflowCancelled",
            "Workflow",
            "Cancelled",
            "Workflow cancelled.",
            time,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    internal async Task<WorkflowExecutionOutcome> ExecuteAsync(
        Guid id,
        ClaimSubmissionRequest request,
        CancellationToken cancellationToken)
    {
        var run = await db.ClaimWorkflowRuns.FindAsync([id], cancellationToken);
        if (run is null)
        {
            return WorkflowExecutionOutcome.Failed(id, "Workflow run was not found.");
        }

        IAsyncDisposable? streamingRunLifetime = null;
        try
        {
            run.Status = "Running";
            await db.SaveChangesAsync(cancellationToken);
            await ClaimExecutor<WorkflowStartMessage, ClaimDraftMessage>.Event(
                db, id, "WorkflowStarted", "Workflow", "Running",
                "Claim review workflow started.", time, cancellationToken);

            logger.LogInformation(
                "RunStreamingAsync started for workflow {WorkflowRunId}.",
                id);
            var streamingRun = await InProcessExecution.RunStreamingAsync(
                factory.Create(),
                new WorkflowStartMessage(id, request),
                cancellationToken: cancellationToken);
            streamingRunLifetime = streamingRun as IAsyncDisposable;
            logger.LogInformation(
                "RunStreamingAsync returned a streaming run for workflow {WorkflowRunId}; watching events.",
                id);
            ClaimDecisionResponse? response = null;

            await foreach (var workflowEvent in streamingRun
                               .WatchStreamAsync()
                               .WithCancellation(cancellationToken))
            {
                if (workflowEvent is RequestInfoEvent requestEvent)
                {
                    logger.LogInformation(
                        "RequestInfoEvent received for workflow {WorkflowRunId}.",
                        id);
                    dynamic nativeRequest = requestEvent.Request;
                    object? requestData = (object?)nativeRequest.Data;
                    string mafRequestId = Convert.ToString((object?)nativeRequest.RequestId)
                        ?? throw new InvalidOperationException(
                            "The native MAF approval request did not provide a request ID.");
                    var approvalRequest = MafApprovalPayload.NormalizeCorrelation(
                        MafApprovalPayload.Deserialize(requestData),
                        id,
                        mafRequestId);

                    var approval = await db.ClaimApprovals.FindAsync(
                        [approvalRequest.ApprovalRequestId],
                        cancellationToken) ?? new ClaimApprovalRecord
                    {
                        ApprovalRequestId = approvalRequest.ApprovalRequestId,
                    };
                    approval.WorkflowRunId = id;
                    approval.MafRequestId = mafRequestId;
                    approval.Status = "Pending";
                    approval.Reason = MafApprovalPayload.NormalizeReason(
                        approvalRequest.ApprovalReason);
                    approval.TriggeredRulesJson = JsonSerializer.Serialize(
                        approvalRequest.TriggeredRules ?? [], JsonOptions);
                    approval.ClaimSnapshotJson = JsonSerializer.Serialize(
                        approvalRequest.Claim, JsonOptions);
                    approval.RiskAssessmentJson = JsonSerializer.Serialize(
                        approvalRequest.RiskAssessment, JsonOptions);
                    approval.ValidationWarningsJson = "[]";
                    var approvalWindow = MafApprovalPayload.NormalizeApprovalWindow(
                        approvalRequest.RequestedAt,
                        approvalRequest.ExpiresAt,
                        time.GetUtcNow(),
                        approvalOptions.Value.ApprovalTimeoutMinutes);
                    approval.RequestedAt = approvalWindow.RequestedAt;
                    approval.ExpiresAt = approvalWindow.ExpiresAt;
                    if (db.Entry(approval).State == EntityState.Detached)
                    {
                        await db.ClaimApprovals.AddAsync(approval, cancellationToken);
                    }

                    run.Status = "WaitingForApproval";
                    run.Error = null;
                    await db.SaveChangesAsync(cancellationToken);
                    logger.LogInformation(
                        "Approval {ApprovalRequestId} persisted for workflow {WorkflowRunId}.",
                        approval.ApprovalRequestId,
                        id);
                    logger.LogInformation(
                        "Approval {ApprovalRequestId} expires at {ExpiresAt}; configured timeout is {TimeoutMinutes} minutes.",
                        approval.ApprovalRequestId,
                        approval.ExpiresAt,
                        Math.Max(1, approvalOptions.Value.ApprovalTimeoutMinutes));
                    logger.LogInformation(
                        "Workflow {WorkflowRunId} status changed to WaitingForApproval.",
                        id);

                    await ClaimExecutor<WorkflowStartMessage, ClaimDraftMessage>.Event(
                        db, id, "ApprovalRequested", "Human Approval", "WaitingForApproval",
                        $"Native MAF approval request {mafRequestId} is waiting.",
                        time, cancellationToken,
                        JsonSerializer.Serialize(new
                        {
                            approvalRequest.ApprovalRequestId,
                            MafRequestId = mafRequestId,
                        }, JsonOptions));
                    logger.LogInformation(
                        "Workflow {WorkflowRunId} suspended durably; releasing streaming execution.",
                        id);
                    return WorkflowExecutionOutcome.Waiting(id, approval.ApprovalRequestId);
                }
                else if (workflowEvent is WorkflowOutputEvent outputEvent
                    && outputEvent.Data is ClaimDecisionResponse claimResponse)
                {
                    response = claimResponse;
                }
            }

            if (response is null)
            {
                throw new InvalidOperationException(
                    "The native streaming workflow completed without a ClaimDecisionResponse.");
            }
            Complete(run, response, response.Status);
            await ClaimExecutor<WorkflowStartMessage, ClaimDraftMessage>.Event(
                db, id, "WorkflowCompleted", "Workflow", response.Status,
                $"Workflow completed with status {response.Status}.", time, cancellationToken);
            return WorkflowExecutionOutcome.Completed(id, response);
        }
        catch (ClaimValidationException exception)
        {
            run.Status = "ValidationFailed";
            run.FailureStage = "ClaimValidationExecutor";
            run.Error = exception.Message;
            run.CompletedAt = time.GetUtcNow();
            await db.SaveChangesAsync(CancellationToken.None);
            return WorkflowExecutionOutcome.Failed(id, exception.Message);
        }
        catch (Exception exception)
        {
            // A failed approval insert remains Added in the change tracker. Detach it
            // before persisting the workflow failure, otherwise every later SaveChanges
            // retries the same invalid INSERT and obscures the original error.
            foreach (var entry in db.ChangeTracker.Entries<ClaimApprovalRecord>()
                         .Where(entry => entry.State == EntityState.Added))
            {
                entry.State = EntityState.Detached;
            }

            run.Status = "Failed";
            run.Error = exception.Message;
            run.CompletedAt = time.GetUtcNow();
            await db.SaveChangesAsync(CancellationToken.None);
            return WorkflowExecutionOutcome.Failed(id, exception.Message);
        }
        finally
        {
            if (streamingRunLifetime is not null)
            {
                await streamingRunLifetime.DisposeAsync();
                logger.LogInformation(
                    "Streaming run disposed for workflow {WorkflowRunId}.",
                    id);
            }

            run.TotalElapsedDurationMs = (long)(time.GetUtcNow() - run.StartedAt).TotalMilliseconds;
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private void Complete(ClaimWorkflowRunRecord run, ClaimDecisionResponse response, string status)
    {
        run.Status = status;
        run.CompletedAt = time.GetUtcNow();
        run.FinalDecisionJson = JsonSerializer.Serialize(response.Decision, JsonOptions);
        run.ExecutionDurationMs = (long)(run.CompletedAt.Value - run.StartedAt).TotalMilliseconds;
        run.TotalElapsedDurationMs = run.ExecutionDurationMs;
    }
}

public static class MafApprovalPayload
{
    public const string DefaultApprovalReason =
        "Human review is required by the claims approval workflow.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static ClaimApprovalRequest Deserialize(object? data)
    {
        var request = data switch
        {
            ClaimApprovalRequest typed => typed,
            PortableValue portable => DeserializePortableValue(portable),
            _ => throw new InvalidOperationException(
                $"Unsupported approval payload type: {data?.GetType().FullName ?? "<null>"}"),
        };

        if (request.Claim is null || request.RiskAssessment is null)
        {
            throw new InvalidOperationException(
                "The MAF approval request is missing its claim or risk assessment payload.");
        }

        return request;
    }

    public static ClaimApprovalRequest NormalizeCorrelation(
        ClaimApprovalRequest request,
        Guid workflowRunId,
        string mafRequestId)
    {
        if (workflowRunId == Guid.Empty)
        {
            throw new ArgumentException("The workflow run ID is required.", nameof(workflowRunId));
        }

        if (request.WorkflowRunId != Guid.Empty && request.WorkflowRunId != workflowRunId)
        {
            throw new InvalidOperationException(
                $"The MAF approval payload belongs to workflow {request.WorkflowRunId}, not {workflowRunId}.");
        }

        var approvalRequestId = request.ApprovalRequestId == Guid.Empty
            ? CreateStableApprovalRequestId(mafRequestId)
            : request.ApprovalRequestId;
        return request with
        {
            ApprovalRequestId = approvalRequestId,
            WorkflowRunId = workflowRunId,
        };
    }

    private static Guid CreateStableApprovalRequestId(string mafRequestId)
    {
        if (string.IsNullOrWhiteSpace(mafRequestId))
        {
            throw new InvalidOperationException("The native MAF request ID is required.");
        }

        if (Guid.TryParse(mafRequestId, out var parsed))
        {
            return parsed;
        }

        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(mafRequestId));
        return new Guid(hash.AsSpan(0, 16));
    }

    public static string NormalizeReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? DefaultApprovalReason : reason;

    public static (DateTimeOffset RequestedAt, DateTimeOffset ExpiresAt) NormalizeApprovalWindow(
        DateTimeOffset requestedAt,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        int timeoutMinutes)
    {
        var normalizedRequestedAt = requestedAt == default ? now : requestedAt;
        var minimumExpiry = normalizedRequestedAt.AddMinutes(Math.Max(1, timeoutMinutes));
        return (
            normalizedRequestedAt,
            expiresAt < minimumExpiry ? minimumExpiry : expiresAt);
    }

    private static ClaimApprovalRequest DeserializePortableValue(PortableValue portable)
    {
        // PortableValue in Microsoft.Agents.AI.Workflows 1.15 does not expose
        // ToObject<T>. Its registered System.Text.Json converter writes the wrapped
        // payload, so round-trip through JsonElement using the runtime type.
        foreach (var property in portable.GetType().GetProperties(
                     System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            var value = property.GetValue(portable);
            if (value is ClaimApprovalRequest typed)
            {
                return typed;
            }

            if (value is not null)
            {
                var propertyElement = JsonSerializer.SerializeToElement(
                    value,
                    value.GetType(),
                    JsonOptions);
                if (TryFindApprovalPayload(propertyElement, out var propertyPayload))
                {
                    return propertyPayload.Deserialize<ClaimApprovalRequest>(JsonOptions)
                        ?? throw new InvalidOperationException(
                            "The MAF approval request property could not be deserialized.");
                }
            }
        }

        var element = JsonSerializer.SerializeToElement(
            portable,
            portable.GetType(),
            JsonOptions);
        if (TryFindApprovalPayload(element, out var payload))
        {
            return payload.Deserialize<ClaimApprovalRequest>(JsonOptions)
                ?? throw new InvalidOperationException(
                    "The MAF approval request payload could not be deserialized.");
        }

        throw new InvalidOperationException(
            $"The PortableValue did not contain a ClaimApprovalRequest payload. JSON: {element}");
    }

    private static bool TryFindApprovalPayload(JsonElement element, out JsonElement payload)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var properties = element.EnumerateObject().ToList();
            var hasClaim = properties.Any(property =>
                property.Name.Equals("Claim", StringComparison.OrdinalIgnoreCase));
            var hasRisk = properties.Any(property =>
                property.Name.Equals("RiskAssessment", StringComparison.OrdinalIgnoreCase));
            if (hasClaim && hasRisk)
            {
                payload = element;
                return true;
            }

            foreach (var property in properties)
            {
                if (TryFindApprovalPayload(property.Value, out payload))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryFindApprovalPayload(item, out payload))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                try
                {
                    using var document = JsonDocument.Parse(value);
                    if (TryFindApprovalPayload(document.RootElement, out var nested))
                    {
                        payload = nested.Clone();
                        return true;
                    }
                }
                catch (JsonException)
                {
                    // Ordinary string values are not nested JSON payloads.
                }
            }
        }

        payload = default;
        return false;
    }
}

public enum WorkflowExecutionStatus
{
    WaitingForApproval,
    Completed,
    Failed,
}

public sealed record WorkflowExecutionOutcome(
    Guid WorkflowRunId,
    WorkflowExecutionStatus Status,
    Guid? ApprovalRequestId,
    ClaimDecisionResponse? Response,
    string? Error)
{
    public static WorkflowExecutionOutcome Waiting(Guid id, Guid approvalId) =>
        new(id, WorkflowExecutionStatus.WaitingForApproval, approvalId, null, null);

    public static WorkflowExecutionOutcome Completed(Guid id, ClaimDecisionResponse response) =>
        new(id, WorkflowExecutionStatus.Completed, null, response, null);

    public static WorkflowExecutionOutcome Failed(Guid id, string error) =>
        new(id, WorkflowExecutionStatus.Failed, null, null, error);
}

public interface IClaimWorkflowQueue
{
    ValueTask EnqueueStartAsync(
        Guid workflowRunId,
        ClaimSubmissionRequest request,
        CancellationToken cancellationToken);

    ValueTask EnqueueResumeAsync(
        NativeApprovalResponseCommand command,
        CancellationToken cancellationToken);

    IAsyncEnumerable<ClaimWorkflowQueueItem> ReadAllAsync(CancellationToken cancellationToken);
}

public abstract record ClaimWorkflowQueueItem
{
    public sealed record Start(Guid WorkflowRunId, ClaimSubmissionRequest Request)
        : ClaimWorkflowQueueItem;

    public sealed record Resume(NativeApprovalResponseCommand Command)
        : ClaimWorkflowQueueItem;
}

public sealed class ClaimWorkflowQueue : IClaimWorkflowQueue
{
    private readonly Channel<ClaimWorkflowQueueItem> _channel =
        Channel.CreateUnbounded<ClaimWorkflowQueueItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    public ValueTask EnqueueStartAsync(
        Guid workflowRunId,
        ClaimSubmissionRequest request,
        CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(
            new ClaimWorkflowQueueItem.Start(workflowRunId, request),
            cancellationToken);

    public ValueTask EnqueueResumeAsync(
        NativeApprovalResponseCommand command,
        CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(new ClaimWorkflowQueueItem.Resume(command), cancellationToken);

    public IAsyncEnumerable<ClaimWorkflowQueueItem> ReadAllAsync(
        CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

public sealed class ClaimWorkflowBackgroundService(
    IClaimWorkflowQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<ClaimWorkflowBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverDecidedApprovalsAsync(stoppingToken);
        await foreach (var item in queue.ReadAllAsync(stoppingToken))
        {
            if (item is ClaimWorkflowQueueItem.Start start)
            {
                logger.LogInformation(
                    "Starting background streaming execution for workflow {WorkflowRunId}.",
                    start.WorkflowRunId);
                await RunStartAsync(start, stoppingToken);
            }
            else if (item is ClaimWorkflowQueueItem.Resume resume)
            {
                await RunResumeAsync(resume, stoppingToken);
            }
        }
    }

    private async Task RecoverDecidedApprovalsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();
        var waitingWorkflowIds = await db.ClaimWorkflowRuns
            .Where(run => run.Status == "WaitingForApproval")
            .Select(run => run.WorkflowRunId)
            .ToListAsync(cancellationToken);
        var decisions = await db.ClaimApprovals
            .Where(approval => waitingWorkflowIds.Contains(approval.WorkflowRunId)
                && (approval.Status == "Approved" || approval.Status == "Rejected"))
            .ToListAsync(cancellationToken);

        foreach (var approval in decisions)
        {
            var outcome = approval.Status == "Approved"
                ? ClaimApprovalOutcome.Approved
                : ClaimApprovalOutcome.Rejected;
            var decision = new ClaimApprovalDecision(
                approval.ApprovalRequestId,
                approval.WorkflowRunId,
                outcome,
                approval.Reviewer ?? "Recovered reviewer",
                approval.Comment,
                approval.DecidedAt ?? approval.RequestedAt);
            await queue.EnqueueResumeAsync(
                new NativeApprovalResponseCommand(
                    approval.WorkflowRunId,
                    approval.ApprovalRequestId,
                    decision),
                cancellationToken);
            logger.LogInformation(
                "Recovered persisted workflow continuation {WorkflowRunId} after startup.",
                approval.WorkflowRunId);
        }
    }

    private async Task RunStartAsync(
        ClaimWorkflowQueueItem.Start start,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var service = (ClaimWorkflowService)scope.ServiceProvider
            .GetRequiredService<IClaimWorkflowService>();
        try
        {
            var outcome = await service.ExecuteAsync(
                start.WorkflowRunId, start.Request, cancellationToken);
            if (outcome.Status == WorkflowExecutionStatus.Completed)
            {
                logger.LogInformation("Workflow {WorkflowRunId} completed.", outcome.WorkflowRunId);
            }
            else if (outcome.Status == WorkflowExecutionStatus.Failed)
            {
                logger.LogError("Workflow {WorkflowRunId} failed: {Error}",
                    outcome.WorkflowRunId, outcome.Error);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Native streaming workflow processing failed.");
        }
        finally
        {
            logger.LogInformation(
                "Background streaming execution exited for workflow {WorkflowRunId}.",
                start.WorkflowRunId);
        }
    }

    private async Task RunResumeAsync(
        ClaimWorkflowQueueItem.Resume resume,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IClaimWorkflowService>();
        await service.ResumeAsync(resume.Command, cancellationToken);
    }
}

public sealed class ApprovalExpiryService(IServiceProvider serviceProvider, TimeProvider time)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var scope = serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();
            var now = time.GetUtcNow();
            var pending = await db.ClaimApprovals
                .Where(approval => approval.Status == "Pending")
                .ToListAsync(stoppingToken);
            var expired = pending.Where(approval => approval.ExpiresAt <= now).ToList();

            foreach (var approval in expired)
            {
                approval.Status = "Expired";
                var run = await db.ClaimWorkflowRuns.FindAsync(
                    [approval.WorkflowRunId],
                    stoppingToken);
                if (run is not null)
                {
                    run.Status = "Expired";
                }
            }

            await db.SaveChangesAsync(stoppingToken);
        }
    }
}
