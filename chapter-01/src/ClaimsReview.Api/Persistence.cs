using System.Text.Json;
using ClaimsReview.Contracts;
using Microsoft.EntityFrameworkCore;
namespace ClaimsReview.Api;
public sealed class ClaimsDbContext(DbContextOptions<ClaimsDbContext> o) : DbContext
(o)
{
    public DbSet<ClaimWorkflowRunRecord> ClaimWorkflowRuns => Set<ClaimWorkflowRunRecord>();
    public DbSet<ClaimApprovalRecord> ClaimApprovals => Set<ClaimApprovalRecord>();
    public DbSet<ClaimWorkflowEventRecord> ClaimWorkflowEvents => Set<ClaimWorkflowEventRecord>();
    public DbSet<ClaimExecutorTraceRecord> ClaimExecutorTraces => Set<ClaimExecutorTraceRecord>();
    public DbSet<ClaimAgentExecutionRecord> ClaimAgentExecutions => Set<ClaimAgentExecutionRecord>();
    public DbSet<ClaimMessageTransitionRecord> ClaimMessageTransitions => Set<ClaimMessageTransitionRecord>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ClaimWorkflowRunRecord>().HasKey(x => x.WorkflowRunId);
        b.Entity<ClaimApprovalRecord>().HasKey(x => x.ApprovalRequestId);
        b.Entity<ClaimApprovalRecord>().HasIndex(x => x.ApprovalRequestId).IsUnique();
        b.Entity<ClaimApprovalRecord>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<ClaimWorkflowEventRecord>().HasKey(x => x.Id);
        b.Entity<ClaimWorkflowEventRecord>().HasIndex(x => new { x.WorkflowRunId, x.Sequence }).IsUnique();
        b.Entity<ClaimExecutorTraceRecord>().HasKey(x => x.Id);
        b.Entity<ClaimAgentExecutionRecord>().HasKey(x => x.Id);
        b.Entity<ClaimMessageTransitionRecord>().HasKey(x => x.Id);
    }
}
public sealed class ClaimWorkflowRunRecord
{
    public Guid WorkflowRunId { get; set; }
    public string PolicyNumber { get; set; } = "";
    public string ClaimantName { get; set; } = "";
    public string ClaimType { get; set; } = "";
    public decimal ClaimedAmount { get; set; }
    public string Status { get; set; } = "Queued";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public long? ExecutionDurationMs { get; set; }
    public long? HumanWaitDurationMs { get; set; }
    public long? TotalElapsedDurationMs { get; set; }
    public string? FailureStage { get; set; }
    public string? Error { get; set; }
    public string? OriginalRequestJson { get; set; }
    public string? FinalDecisionJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
public sealed class ClaimApprovalRecord
{
    public Guid ApprovalRequestId { get; set; }
    public Guid WorkflowRunId { get; set; }
    public string MafRequestId { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public string Reason { get; set; } = "";
    public string TriggeredRulesJson { get; set; } = "[]";
    public string ClaimSnapshotJson { get; set; } = "{}";
    public string RiskAssessmentJson { get; set; } = "{}";
    public string ValidationWarningsJson { get; set; } = "[]";
    public string? Reviewer { get; set; }
    public string? Comment { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public int Version { get; set; }
}
public sealed class ClaimWorkflowEventRecord
{
    public long Id { get; set; }
    public long Sequence { get; set; }
    public Guid WorkflowRunId { get; set; }
    public string EventType { get; set; } = "";
    public string Stage { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Summary { get; set; }
    public string? DataJson { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
public sealed class ClaimExecutorTraceRecord
{
    public long Id { get; set; }
    public Guid WorkflowRunId { get; set; }
    public string ExecutorName { get; set; } = "";
    public string Status { get; set; } = "";
    public long DurationMs { get; set; }
    public string? Error { get; set; }
}
public sealed class ClaimAgentExecutionRecord
{
    public long Id { get; set; }
    public Guid WorkflowRunId { get; set; }
    public string ExecutorName { get; set; } = "";
    public string AgentName { get; set; } = "";
    public string ResponseType { get; set; } = "";
    public string Status { get; set; } = "";
    public int AttemptCount { get; set; }
    public long DurationMs { get; set; }
    public string? FailureKind { get; set; }
    public string? FailureCode { get; set; }
    public string? FailurePath { get; set; }
    public string WarningsJson { get; set; } = "[]";
}
public sealed class ClaimMessageTransitionRecord
{
    public long Id { get; set; }
    public Guid WorkflowRunId { get; set; }
    public string FromMessageType { get; set; } = "";
    public string ToMessageType { get; set; } = "";
    public string CarriedForwardJson { get; set; } = "[]";
    public string AddedJson { get; set; } = "[]";
    public string ChangedJson { get; set; } = "[]";
    public string RemovedJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }
}
public enum ApprovalDecisionStatus
{
    Accepted,
    AlreadyDecided,
    Conflict,
    Expired,
    NotFound
}
public sealed record ApprovalDecisionResult(ApprovalDecisionStatus Status, ClaimApprovalRecord? Approval);
public interface IClaimApprovalStore
{
    Task<ClaimApprovalRecord?> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<ClaimApprovalSummary>> GetAllAsync(string? status, CancellationToken ct);
    Task<IReadOnlyList<ClaimApprovalSummary>> GetPendingAsync(CancellationToken ct);
    Task<ApprovalDecisionResult> DecideAsync(ClaimApprovalDecision d, CancellationToken ct);
}
public sealed class EfClaimApprovalStore(ClaimsDbContext db, TimeProvider time) : IClaimApprovalStore
{
    static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
    public Task<ClaimApprovalRecord?> GetAsync(Guid id,
                                               CancellationToken ct) => db.ClaimApprovals.FindAsync([id], ct).AsTask();
    public async Task<IReadOnlyList<ClaimApprovalSummary>> GetAllAsync(string? status, CancellationToken ct)
    {
        var rows = await db.ClaimApprovals.ToListAsync(ct);
        return rows
            .Where(x => string.IsNullOrWhiteSpace(status) ||
                        string.Equals(x.Status, status, StringComparison.OrdinalIgnoreCase))
            .Where(x => x.Status != "Pending" || x.ExpiresAt > time.GetUtcNow())
            .Select(ToSummary)
            .ToList();
    }
    public Task<IReadOnlyList<ClaimApprovalSummary>> GetPendingAsync(CancellationToken ct) => GetAllAsync("Pending",
                                                                                                          ct);
    public async Task<ApprovalDecisionResult> DecideAsync(ClaimApprovalDecision d, CancellationToken ct)
    {
        var a = await GetAsync(d.ApprovalRequestId, ct);
        if (a is null)
            return new(ApprovalDecisionStatus.NotFound, null);
        if (a.Status == "Expired" || a.ExpiresAt <= time.GetUtcNow())
            return new(ApprovalDecisionStatus.Expired, a);
        if (a.Status != "Pending")
            return string.Equals(a.Status, d.Outcome.ToString(), StringComparison.Ordinal) &&
                           string.Equals(a.Reviewer, d.Reviewer, StringComparison.Ordinal) && a.DecidedAt is not null
                       ? new(ApprovalDecisionStatus.AlreadyDecided, a)
                       : new(ApprovalDecisionStatus.Conflict, a);
        a.Status = d.Outcome.ToString();
        a.Reviewer = d.Reviewer;
        a.Comment = d.Comment;
        a.DecidedAt = d.DecidedAt;
        a.Version++;
        await db.SaveChangesAsync(ct);
        return new(ApprovalDecisionStatus.Accepted, a);
    }
    static ClaimApprovalSummary ToSummary(ClaimApprovalRecord a)
    {
        var c = JsonSerializer.Deserialize<ValidatedClaim>(a.ClaimSnapshotJson, J)!;
        var r = JsonSerializer.Deserialize<ClaimRiskAssessment>(a.RiskAssessmentJson, J)!;
        return new(a.ApprovalRequestId, a.WorkflowRunId, c.PolicyNumber, c.ClaimantName, c.ClaimedAmount,
                   r.RiskLevel.ToString(), r.RiskScore, a.Reason, a.RequestedAt, a.ExpiresAt, a.Status);
    }
}
