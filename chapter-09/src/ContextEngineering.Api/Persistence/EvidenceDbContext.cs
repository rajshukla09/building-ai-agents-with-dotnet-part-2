using Microsoft.EntityFrameworkCore;

namespace ContextEngineering.Api.Persistence;

public sealed class EvidenceDbContext(DbContextOptions<EvidenceDbContext> options)
    : DbContext(options)
{
    public DbSet<EvidenceRecord> Evidence => Set<EvidenceRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var evidence = modelBuilder.Entity<EvidenceRecord>();

        evidence.HasKey(item => item.EvidenceId);
        evidence.Property(item => item.SourceAgent).HasMaxLength(100).IsRequired();
        evidence.Property(item => item.Component).HasMaxLength(200).IsRequired();
        evidence.Property(item => item.Summary).HasMaxLength(1000).IsRequired();
        evidence.Property(item => item.DetailedContent).IsRequired();
        evidence.HasIndex(item => new { item.IncidentId, item.CreatedAt });
        evidence.HasIndex(item => new { item.IncidentId, item.EvidenceType });
    }
}
