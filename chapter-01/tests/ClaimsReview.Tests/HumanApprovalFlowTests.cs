using ClaimsReview.Api;
using ClaimsReview.Api.Controllers;
using ClaimsReview.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Agents.AI.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

public sealed class HumanApprovalFlowTests
{
    [Fact]
    public async Task Low_risk_claim_completes_without_creating_pending_approval()
    {
        await using var db = CreateDatabase();
        var service = CreateWorkflowService(db);
        var request = LowRiskClaim();
        var workflowId = await SeedRunAsync(db, request);

        var outcome = await service.ExecuteAsync(workflowId, request, CancellationToken.None);

        Assert.Equal(WorkflowExecutionStatus.Completed, outcome.Status);
        Assert.Equal("Completed", outcome.Response!.Status);
        Assert.Equal("Approved", outcome.Response.Decision!.Outcome);
        Assert.Null(outcome.Response.ApprovalRequestId);
        Assert.Empty(await db.ClaimApprovals.ToListAsync());
        Assert.Equal("Completed", (await db.ClaimWorkflowRuns.FindAsync(workflowId))!.Status);
        var executors = await ExecutorNamesAsync(db, workflowId);
        Assert.Contains(nameof(RiskAssessmentAgentExecutor), executors);
        Assert.Contains(nameof(ClaimDecisionExecutor), executors);
        Assert.DoesNotContain(nameof(HumanApprovalRequestExecutor), executors);
    }

    [Fact]
    public async Task Review_required_claim_suspends_and_appears_in_pending_approvals()
    {
        await using var db = CreateDatabase();
        var service = CreateWorkflowService(db);
        var request = ReviewRequiredClaim();
        var workflowId = await SeedRunAsync(db, request);

        var outcome = await service.ExecuteAsync(workflowId, request, CancellationToken.None);

        Assert.Equal(WorkflowExecutionStatus.WaitingForApproval, outcome.Status);
        var approval = Assert.Single(await db.ClaimApprovals.ToListAsync());
        Assert.Equal(outcome.ApprovalRequestId, approval.ApprovalRequestId);
        Assert.Equal(workflowId, approval.WorkflowRunId);
        Assert.Equal("Pending", approval.Status);
        var pending = await new EfClaimApprovalStore(db, TimeProvider.System)
            .GetPendingAsync(CancellationToken.None);
        Assert.Equal(approval.ApprovalRequestId, Assert.Single(pending).ApprovalRequestId);
        var executors = await ExecutorNamesAsync(db, workflowId);
        Assert.Contains(nameof(RiskAssessmentAgentExecutor), executors);
        Assert.Contains(nameof(HumanApprovalRequestExecutor), executors);
        Assert.DoesNotContain(nameof(ClaimDecisionExecutor), executors);
    }

    [Theory]
    [InlineData(ClaimApprovalOutcome.Approved, "Completed")]
    [InlineData(ClaimApprovalOutcome.Rejected, "Rejected")]
    public async Task Human_decision_resumes_review_required_workflow(
        ClaimApprovalOutcome decisionOutcome,
        string expectedStatus)
    {
        await using var db = CreateDatabase();
        var service = CreateWorkflowService(db);
        var request = ReviewRequiredClaim();
        var workflowId = await SeedRunAsync(db, request);
        var waiting = await service.ExecuteAsync(workflowId, request, CancellationToken.None);
        var approvalId = waiting.ApprovalRequestId!.Value;
        var decision = new ClaimApprovalDecision(
            approvalId,
            workflowId,
            decisionOutcome,
            "Reviewer",
            null,
            DateTimeOffset.UtcNow);
        var store = new EfClaimApprovalStore(db, TimeProvider.System);
        var stored = await store.DecideAsync(decision, CancellationToken.None);

        var response = await service.ResumeAsync(
            new NativeApprovalResponseCommand(workflowId, approvalId, decision),
            CancellationToken.None);

        Assert.Equal(ApprovalDecisionStatus.Accepted, stored.Status);
        Assert.NotNull(response);
        Assert.Equal(expectedStatus, response.Status);
        Assert.Equal(decisionOutcome.ToString(), response.Decision!.Outcome);
        Assert.Equal(expectedStatus, (await db.ClaimWorkflowRuns.FindAsync(workflowId))!.Status);
        Assert.Contains(
            nameof(ClaimDecisionExecutor),
            await ExecutorNamesAsync(db, workflowId));
    }

    [Fact]
    public void Missing_portable_correlation_is_restored_from_native_request_context()
    {
        var workflowId = Guid.NewGuid();
        var request = NativeRequest(Guid.Empty, Guid.Empty);

        var normalized = MafApprovalPayload.NormalizeCorrelation(
            request,
            workflowId,
            "native-request-123");

        Assert.Equal(workflowId, normalized.WorkflowRunId);
        Assert.NotEqual(Guid.Empty, normalized.ApprovalRequestId);
        Assert.Equal(
            normalized.ApprovalRequestId,
            MafApprovalPayload.NormalizeCorrelation(request, workflowId, "native-request-123")
                .ApprovalRequestId);
    }

    [Fact]
    public void Portable_approval_window_cannot_expire_before_configured_timeout()
    {
        var now = DateTimeOffset.UtcNow;

        var normalized = MafApprovalPayload.NormalizeApprovalWindow(
            default,
            now.AddSeconds(10),
            now,
            30);

        Assert.Equal(now, normalized.RequestedAt);
        Assert.Equal(now.AddMinutes(30), normalized.ExpiresAt);
    }

    [Fact]
    public async Task Missing_portable_approval_reason_is_normalized_before_insert()
    {
        await using var db = CreateDatabase();
        var approval = PendingApproval(DateTimeOffset.UtcNow);
        approval.Reason = MafApprovalPayload.NormalizeReason(null);
        PopulateApprovalSnapshot(approval);

        db.ClaimApprovals.Add(approval);
        await db.SaveChangesAsync();

        var saved = await db.ClaimApprovals.FindAsync(approval.ApprovalRequestId);
        Assert.Equal(MafApprovalPayload.DefaultApprovalReason, saved!.Reason);
    }

    [Fact]
    public async Task Portable_MAF_approval_payload_deserializes_to_typed_request()
    {
        var expected = NativeRequest(Guid.NewGuid(), Guid.NewGuid());
        var source = new PortableApprovalRequestSource();
        var requestPort = RequestPort.Create<ClaimApprovalRequest, ClaimApprovalDecision>(
            "portable-approval-test");
        var workflow = new WorkflowBuilder(source)
            .AddEdge(source, requestPort)
            .Build();
        var streamingRun = await InProcessExecution.RunStreamingAsync(workflow, expected);
        PortableValue? portable = null;

        try
        {
            await foreach (var workflowEvent in streamingRun.WatchStreamAsync())
            {
                if (workflowEvent is not RequestInfoEvent requestEvent)
                {
                    continue;
                }

                dynamic nativeRequest = requestEvent.Request;
                portable = (PortableValue)nativeRequest.Data;
                break;
            }
        }
        finally
        {
            if (streamingRun is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();
            }
        }

        Assert.NotNull(portable);
        var actual = MafApprovalPayload.Deserialize(portable);

        // JSON deserialization materializes collection properties as List<T>, while
        // the request fixture uses collection-expression read-only lists. Record
        // equality compares those collection instances rather than their elements,
        // so compare the complete object graph structurally.
        Assert.Equivalent(expected, actual, strict: true);
    }

    [Fact]
    public async Task Workflow_status_selects_latest_approval_with_sqlite()
    {
        await using var db = CreateDatabase();
        var workflowId = Guid.NewGuid();
        var older = PendingApproval(DateTimeOffset.UtcNow.AddMinutes(-5));
        older.WorkflowRunId = workflowId;
        var newest = PendingApproval(DateTimeOffset.UtcNow);
        newest.WorkflowRunId = workflowId;
        db.ClaimWorkflowRuns.Add(new ClaimWorkflowRunRecord
        {
            WorkflowRunId = workflowId,
            Status = "WaitingForApproval",
            StartedAt = DateTimeOffset.UtcNow,
        });
        db.ClaimApprovals.AddRange(older, newest);
        await db.SaveChangesAsync();
        var controller = new ClaimWorkflowsController(null!, db);

        var result = await controller.Get(workflowId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var workflow = Assert.IsType<ClaimWorkflowRunDto>(ok.Value);
        Assert.Equal(newest.ApprovalRequestId, workflow.ApprovalRequestId);
    }

    [Fact]
    public async Task Workflow_queue_preserves_typed_resume_command()
    {
        var queue = new ClaimWorkflowQueue();
        var approvalId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var request = new ClaimApprovalRequest(
            approvalId,
            workflowId,
            new ValidatedClaim("POL-1", "Claimant", "VehicleDamage", "Damage", 100,
                DateOnly.FromDateTime(DateTime.UtcNow), ["photo.jpg"]),
            new ClaimRiskAssessment(ClaimRiskLevel.High, 80, ["High risk"], "Review", true),
            "Human review required",
            ["HighRisk"],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(30));
        var decision = new ClaimApprovalDecision(
            approvalId,
            workflowId,
            ClaimApprovalOutcome.Approved,
            "Reviewer",
            null,
            DateTimeOffset.UtcNow);
        var command = new NativeApprovalResponseCommand(workflowId, approvalId, decision);

        await queue.EnqueueResumeAsync(command, CancellationToken.None);

        await using var enumerator = queue.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
        var queued = Assert.IsType<ClaimWorkflowQueueItem.Resume>(enumerator.Current);
        Assert.Equal(command, queued.Command);
    }

    [Fact]
    public async Task Duplicate_approval_is_idempotent_but_rejection_conflicts()
    {
        await using var db = CreateDatabase();
        var time = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var approval = PendingApproval(time.GetUtcNow());
        db.ClaimApprovals.Add(approval);
        await db.SaveChangesAsync();
        var store = new EfClaimApprovalStore(db, time);
        var approved = new ClaimApprovalDecision(
            approval.ApprovalRequestId,
            approval.WorkflowRunId,
            ClaimApprovalOutcome.Approved,
            "Reviewer",
            null,
            time.GetUtcNow());

        var first = await store.DecideAsync(approved, CancellationToken.None);
        var duplicate = await store.DecideAsync(approved, CancellationToken.None);
        var conflict = await store.DecideAsync(
            approved with { Outcome = ClaimApprovalOutcome.Rejected },
            CancellationToken.None);

        Assert.Equal(ApprovalDecisionStatus.Accepted, first.Status);
        Assert.Equal(ApprovalDecisionStatus.AlreadyDecided, duplicate.Status);
        Assert.Equal(ApprovalDecisionStatus.Conflict, conflict.Status);
    }

    [Fact]
    public async Task Persisted_decision_continuation_survives_service_restart()
    {
        await using var db = CreateDatabase();
        var approval = PendingApproval(DateTimeOffset.UtcNow);
        approval.Status = "Approved";
        PopulateApprovalSnapshot(approval);
        db.ClaimApprovals.Add(approval);
        await db.SaveChangesAsync();
        var decision = NativeDecision(
            NativeRequest(approval.WorkflowRunId, approval.ApprovalRequestId),
            ClaimApprovalOutcome.Approved);
        var executor = new ClaimDecisionExecutor(db, TimeProvider.System);
        var workflow = new ClaimReviewWorkflow(null!, null!, null!, null!, executor)
            .CreateDecisionContinuation();

        var run = await Microsoft.Agents.AI.Workflows.InProcessExecution.RunAsync(
            workflow,
            decision,
            cancellationToken: CancellationToken.None);

        var response = run.NewEvents
            .OfType<Microsoft.Agents.AI.Workflows.WorkflowOutputEvent>()
            .Select(item => item.Data)
            .OfType<ClaimDecisionResponse>()
            .Single();
        Assert.Equal("Completed", response.Status);
        Assert.Equal(approval.WorkflowRunId, response.WorkflowRunId);
    }

    [Fact]
    public async Task Waiting_runs_require_no_live_continuation_registry()
    {
        await using var db = CreateDatabase();
        for (var index = 0; index < 100; index++)
        {
            var approval = PendingApproval(DateTimeOffset.UtcNow);
            PopulateApprovalSnapshot(approval);
            db.ClaimApprovals.Add(approval);
        }

        await db.SaveChangesAsync();

        Assert.Equal(100, await db.ClaimApprovals.CountAsync());
        Assert.DoesNotContain(
            typeof(ClaimWorkflowService).Assembly.GetTypes(),
            type => type.Name.Contains("NativeApprovalRunRegistry", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Expired_approval_cannot_be_decided()
    {
        await using var db = CreateDatabase();
        var time = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var approval = PendingApproval(time.GetUtcNow().AddHours(-1));
        approval.ExpiresAt = time.GetUtcNow().AddMinutes(-1);
        db.ClaimApprovals.Add(approval);
        await db.SaveChangesAsync();
        var result = await new EfClaimApprovalStore(db, time).DecideAsync(
            new ClaimApprovalDecision(approval.ApprovalRequestId, approval.WorkflowRunId,
                ClaimApprovalOutcome.Approved, "Reviewer", null, time.GetUtcNow()),
            CancellationToken.None);

        Assert.Equal(ApprovalDecisionStatus.Expired, result.Status);
    }

    [Fact]
    public async Task Pending_list_excludes_expired_approvals()
    {
        await using var db = CreateDatabase();
        var time = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var current = PendingApproval(time.GetUtcNow());
        PopulateApprovalSnapshot(current);
        var expired = PendingApproval(time.GetUtcNow().AddHours(-1));
        expired.ExpiresAt = time.GetUtcNow().AddMinutes(-1);
        PopulateApprovalSnapshot(expired);
        db.ClaimApprovals.AddRange(current, expired);
        await db.SaveChangesAsync();

        var pending = await new EfClaimApprovalStore(db, time)
            .GetPendingAsync(CancellationToken.None);

        var item = Assert.Single(pending);
        Assert.Equal(current.ApprovalRequestId, item.ApprovalRequestId);
        Assert.Equal("Pending", item.Status);
    }

    [Fact]
    public async Task Approved_list_returns_approved_claims_with_summary_details()
    {
        await using var db = CreateDatabase();
        var time = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var approved = PendingApproval(time.GetUtcNow());
        approved.Status = "Approved";
        approved.Reviewer = "Reviewer";
        approved.DecidedAt = time.GetUtcNow();
        PopulateApprovalSnapshot(approved);
        db.ClaimApprovals.Add(approved);
        await db.SaveChangesAsync();

        var results = await new EfClaimApprovalStore(db, time)
            .GetAllAsync("Approved", CancellationToken.None);

        var item = Assert.Single(results);
        Assert.Equal(approved.ApprovalRequestId, item.ApprovalRequestId);
        Assert.Equal("Claimant", item.ClaimantName);
        Assert.Equal(100, item.ClaimedAmount);
        Assert.Equal("Approved", item.Status);
    }

    private static ClaimsDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<ClaimsDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        var db = new ClaimsDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }

    private static ClaimWorkflowService CreateWorkflowService(ClaimsDbContext db)
    {
        var options = Options.Create(new HumanApprovalOptions
        {
            Enabled = true,
            AmountThreshold = 50_000,
            HighRiskScoreThreshold = 70,
            ApprovalTimeoutMinutes = 30,
        });
        var time = TimeProvider.System;
        var policy = new HumanApprovalPolicy(options);
        var workflow = new ClaimReviewWorkflow(
            new ClaimIntakeAgentExecutor(new TestClaimIntakeAgent(), db, time),
            new ClaimValidationExecutor(new ClaimValidator(), db, time),
            new RiskAssessmentAgentExecutor(new TestRiskAssessmentAgent(), policy, db, time),
            new HumanApprovalRequestExecutor(options, db, time),
            new ClaimDecisionExecutor(db, time));
        return new ClaimWorkflowService(
            workflow,
            db,
            time,
            new ClaimWorkflowQueue(),
            options,
            NullLogger<ClaimWorkflowService>.Instance);
    }

    private static async Task<Guid> SeedRunAsync(
        ClaimsDbContext db,
        ClaimSubmissionRequest request)
    {
        var workflowId = Guid.NewGuid();
        db.ClaimWorkflowRuns.Add(new ClaimWorkflowRunRecord
        {
            WorkflowRunId = workflowId,
            PolicyNumber = request.PolicyNumber,
            ClaimantName = request.ClaimantName,
            ClaimType = request.ClaimType,
            ClaimedAmount = request.ClaimedAmount,
            Status = "Queued",
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            OriginalRequestJson = System.Text.Json.JsonSerializer.Serialize(request),
        });
        await db.SaveChangesAsync();
        return workflowId;
    }

    private static Task<List<string>> ExecutorNamesAsync(
        ClaimsDbContext db,
        Guid workflowId) =>
        db.ClaimExecutorTraces
            .Where(trace => trace.WorkflowRunId == workflowId)
            .Select(trace => trace.ExecutorName)
            .ToListAsync();

    private static ClaimSubmissionRequest LowRiskClaim() => new(
        "POL-LOW",
        "Low Risk Claimant",
        "VehicleDamage",
        "Minor routine damage",
        1_000,
        DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
        ["photo-front.jpg", "photo-side.jpg"]);

    private static ClaimSubmissionRequest ReviewRequiredClaim() => new(
        "POL-REVIEW",
        "Review Claimant",
        "VehicleDamage",
        "Possible fraud indicator",
        75_000,
        DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
        ["photo-front.jpg", "photo-side.jpg"]);

    private static ClaimApprovalRecord PendingApproval(DateTimeOffset now) => new()
    {
        ApprovalRequestId = Guid.NewGuid(),
        WorkflowRunId = Guid.NewGuid(),
        Status = "Pending",
        RequestedAt = now,
        ExpiresAt = now.AddMinutes(30),
    };

    private static void PopulateApprovalSnapshot(ClaimApprovalRecord approval)
    {
        var request = NativeRequest(approval.WorkflowRunId, approval.ApprovalRequestId);
        approval.ClaimSnapshotJson = System.Text.Json.JsonSerializer.Serialize(request.Claim);
        approval.RiskAssessmentJson = System.Text.Json.JsonSerializer.Serialize(request.RiskAssessment);
    }

    private static ClaimApprovalRequest NativeRequest(Guid workflowId, Guid approvalId) => new(
        approvalId,
        workflowId,
        new ValidatedClaim("POL-1", "Claimant", "VehicleDamage", "Damage", 100,
            DateOnly.FromDateTime(DateTime.UtcNow), ["photo.jpg"]),
        new ClaimRiskAssessment(ClaimRiskLevel.High, 80, ["High risk"], "Review", true),
        "Human review required",
        ["HighRisk"],
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow.AddMinutes(30));

    private static ClaimApprovalDecision NativeDecision(
        ClaimApprovalRequest request,
        ClaimApprovalOutcome outcome) => new(
        request.ApprovalRequestId,
        request.WorkflowRunId,
        outcome,
        "Reviewer",
        null,
        DateTimeOffset.UtcNow);

    private sealed class PortableApprovalRequestSource()
        : Executor<ClaimApprovalRequest, ClaimApprovalRequest>(nameof(PortableApprovalRequestSource))
    {
        public override ValueTask<ClaimApprovalRequest> HandleAsync(
            ClaimApprovalRequest message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(message);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
