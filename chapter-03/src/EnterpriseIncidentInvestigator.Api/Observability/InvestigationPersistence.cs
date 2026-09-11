using EnterpriseIncidentInvestigator.Api.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace EnterpriseIncidentInvestigator.Api.Observability;

public static class InvestigationEventTypes
{
    public const string Started = "InvestigationStarted";
    public const string SupervisorDecision = "SupervisorDecision";
    public const string AgentStarted = "AgentStarted";
    public const string McpCapabilitiesDiscovered = "McpCapabilitiesDiscovered";
    public const string ToolCompleted = "McpToolCompleted";
    public const string AgentCompleted = "AgentCompleted";
    public const string AgentFailed = "AgentFailed";
    public const string LimitReached = "InvestigationLimitReached";
    public const string RootCauseGenerated = "RootCauseGenerated";
    public const string Completed = "InvestigationCompleted";
    public const string Failed = "InvestigationFailed";
}

public sealed class InvestigationRunEntity
{
    public Guid Id { get; set; }
    public string Incident { get; set; } = "";
    public string Status { get; set; } = "Queued";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ResultJson { get; set; }
    public List<InvestigationEventEntity> Events { get; set; } = [];
}

public sealed class InvestigationEventEntity
{
    public long Id { get; set; }
    public Guid RunId { get; set; }
    public InvestigationRunEntity Run { get; set; } = null!;
    public int Sequence { get; set; }
    public string EventType { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; }
    public string? Agent { get; set; }
    public int? AgentInvocationSequence { get; set; }
    public string? ToolServer { get; set; }
    public string? Tool { get; set; }
    public int? ToolSequence { get; set; }
    public string Status { get; set; } = "";
    public string? Reason { get; set; }
    public string? InputContextSummary { get; set; }
    public string? FindingSummary { get; set; }
    public string? SuggestedNextInvestigation { get; set; }
    public double? Confidence { get; set; }
    public long? DurationMilliseconds { get; set; }
    public string? ErrorSummary { get; set; }
}

public sealed class InvestigationDbContext(DbContextOptions<InvestigationDbContext> options) : DbContext
(options)
{
    public DbSet<InvestigationRunEntity> Runs => Set<InvestigationRunEntity>();
    public DbSet<InvestigationEventEntity> Events => Set<InvestigationEventEntity>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<InvestigationRunEntity>().HasKey(x => x.Id);
        model.Entity<InvestigationEventEntity>().HasIndex(x => new { x.RunId, x.Sequence }).IsUnique();
        model.Entity<InvestigationRunEntity>()
            .HasMany(x => x.Events)
            .WithOne(x => x.Run)
            .HasForeignKey(x => x.RunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed record NewInvestigationEvent(string EventType, string Status, string? Agent = null,
                                           int? AgentInvocationSequence = null, string? ToolServer = null,
                                           string? Tool = null, int? ToolSequence = null, string? Reason = null,
                                           string? InputContextSummary = null, string? FindingSummary = null,
                                           string? SuggestedNextInvestigation = null, double? Confidence = null,
                                           long? DurationMilliseconds = null, string? ErrorSummary = null);

public interface IInvestigationEventStore
{
    Task CreateAsync(Guid runId, string incident, CancellationToken ct);
    Task AppendAsync(Guid runId, NewInvestigationEvent item, CancellationToken ct);
    Task CompleteAsync(Guid runId, string status, InvestigationResponse response, CancellationToken ct);
    Task<IReadOnlyList<InvestigationRunSummary>> ListAsync(CancellationToken ct);
    Task<InvestigationRunView?> GetAsync(Guid runId, int maxAgents, int maxTools, CancellationToken ct);
}

public sealed class InvestigationHub : Hub

{
    public Task WatchRun(Guid runId) => Groups.AddToGroupAsync(Context.ConnectionId, runId.ToString("N"));
}

public interface IInvestigationUpdatePublisher

{
    Task PublishAsync(Guid runId, CancellationToken ct);
}

public sealed class SignalRInvestigationUpdatePublisher(IHubContext<InvestigationHub> hub)
    : IInvestigationUpdatePublisher
{
    public Task PublishAsync(Guid runId, CancellationToken ct) =>
        hub.Clients.Group(runId.ToString("N")).SendAsync("InvestigationUpdated", runId, ct);
}

public sealed class SqliteInvestigationEventStore(InvestigationDbContext db, IInvestigationUpdatePublisher publisher)
    : IInvestigationEventStore
{
    private readonly SemaphoreSlim _writes = new(1, 1);
    private int _sequence;

    public async Task CreateAsync(Guid id, string incident, CancellationToken ct)
    {
        await _writes.WaitAsync(ct);
        try
        {
            var now = DateTimeOffset.UtcNow;
            db.Runs.Add(new() { Id = id, Incident = incident, Status = "Running", StartedAt = now });
            db.Events.Add(Entity(id, _sequence = 1, new(InvestigationEventTypes.Started, "Running"), now));
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            _writes.Release();
        }
        await Publish(id, ct);
    }

    public async Task AppendAsync(Guid id, NewInvestigationEvent item, CancellationToken ct)
    {
        await _writes.WaitAsync(ct);
        try
        {
            db.Events.Add(Entity(id, ++_sequence, item, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            _writes.Release();
        }
        await Publish(id, ct);
    }

    public async Task CompleteAsync(Guid id, string status, InvestigationResponse response, CancellationToken ct)
    {
        await _writes.WaitAsync(ct);
        try
        {
            var run = await db.Runs.SingleAsync(x => x.Id == id, ct);
            run.Status = status;
            run.CompletedAt = DateTimeOffset.UtcNow;
            run.ResultJson = JsonSerializer.Serialize(response);
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            _writes.Release();
        }
        await Publish(id, ct);
    }

    public async Task<IReadOnlyList<InvestigationRunSummary>> ListAsync(CancellationToken ct)
    {
        var runs = await db.Runs.AsNoTracking().Include(x => x.Events).ToListAsync(ct);
        return runs
            .OrderByDescending(x => x.StartedAt)
            .Take(50)
            .Select(x => new InvestigationRunSummary(
                x.Id,
                x.Incident,
                x.Status,
                x.StartedAt,
                x.CompletedAt,
                x.CompletedAt == null
                    ? null
                    : (long?)(x.CompletedAt.Value - x.StartedAt).TotalMilliseconds,
                x.Events
                    .Where(e => e.EventType == InvestigationEventTypes.AgentCompleted)
                    .Select(e => e.Agent!)
                    .Distinct()
                    .ToArray()))
            .ToArray();
    }

    public async Task<InvestigationRunView?> GetAsync(Guid id, int maxAgents, int maxTools, CancellationToken ct)
    {
        var run = await db.Runs.AsNoTracking().Include(x => x.Events).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (run is null)
            return null;
        var summary = new InvestigationRunSummary(
            run.Id, run.Incident, run.Status, run.StartedAt, run.CompletedAt,
            run.CompletedAt is null ? null : (long)(run.CompletedAt.Value - run.StartedAt).TotalMilliseconds,
            run.Events.Where(x => x.EventType == InvestigationEventTypes.AgentCompleted)
                .Select(x => x.Agent!)
                .Distinct()
                .ToArray());
        return new(summary, run.Events.OrderBy(x => x.Sequence).Select(View).ToArray(),
                   run.ResultJson is null ? null : JsonSerializer.Deserialize<InvestigationResponse>(run.ResultJson),
                   maxAgents, maxTools);
    }

    private Task Publish(Guid id, CancellationToken ct) => publisher.PublishAsync(id, ct);

    private static InvestigationEventEntity
    Entity(Guid id, int sequence, NewInvestigationEvent x,
           DateTimeOffset time) => new() { RunId = id,
                                           Sequence = sequence,
                                           EventType = x.EventType,
                                           Timestamp = time,
                                           Agent = x.Agent,
                                           AgentInvocationSequence = x.AgentInvocationSequence,
                                           ToolServer = x.ToolServer,
                                           Tool = x.Tool,
                                           ToolSequence = x.ToolSequence,
                                           Status = x.Status,
                                           Reason = x.Reason,
                                           InputContextSummary = x.InputContextSummary,
                                           FindingSummary = x.FindingSummary,
                                           SuggestedNextInvestigation = x.SuggestedNextInvestigation,
                                           Confidence = x.Confidence,
                                           DurationMilliseconds = x.DurationMilliseconds,
                                           ErrorSummary = x.ErrorSummary };

    private static InvestigationEventView View(InvestigationEventEntity x) =>
        new(x.RunId, x.Sequence, x.EventType, x.Timestamp, x.Agent, x.AgentInvocationSequence, x.ToolServer, x.Tool,
            x.ToolSequence, x.Status, x.Reason, x.InputContextSummary, x.FindingSummary, x.SuggestedNextInvestigation,
            x.Confidence, x.DurationMilliseconds, x.ErrorSummary);
}
