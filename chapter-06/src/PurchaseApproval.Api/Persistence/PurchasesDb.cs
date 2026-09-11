using Microsoft.EntityFrameworkCore;

namespace PurchaseApproval.Api;

public sealed class PurchaseRun
{
    public Guid Id { get; set; }

    public string Description { get; set; } = "";

    public decimal Amount { get; set; }

    public PurchaseStatus Status { get; set; }

    // Retained only for compatibility with the chapter's existing SQLite schema.
    // Durable Task metadata, not these columns, is the workflow state source.
    public string? PendingStep { get; set; }

    public string CompletedSteps { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string? OrderReference { get; set; }

    public int ValidateExecutions { get; set; }

    public int BudgetExecutions { get; set; }

    public int ProcurementExecutions { get; set; }
}

public sealed class ProcurementRecord
{
    public int Id { get; set; }

    public string IdempotencyKey { get; set; } = "";

    public Guid RunId { get; set; }

    public string OrderReference { get; set; } = "";
}

public sealed class PurchasesDb(DbContextOptions<PurchasesDb> options) : DbContext(options)
{
    public DbSet<PurchaseRun> Runs => Set<PurchaseRun>();

    public DbSet<ProcurementRecord> Procurements => Set<ProcurementRecord>();

    protected override void OnModelCreating(ModelBuilder builder) =>
        builder.Entity<ProcurementRecord>().HasIndex(record => record.IdempotencyKey).IsUnique();
}
