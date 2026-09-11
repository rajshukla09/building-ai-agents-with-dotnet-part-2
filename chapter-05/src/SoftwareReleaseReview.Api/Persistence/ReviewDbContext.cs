using Microsoft.EntityFrameworkCore;
using SoftwareReleaseReview.Api.Persistence.Entities;

namespace SoftwareReleaseReview.Api.Persistence;

public sealed class ReviewDbContext(DbContextOptions<ReviewDbContext> options) : DbContext(options)
{
    public DbSet<ReviewRunEntity> ReviewRuns => Set<ReviewRunEntity>();
    public DbSet<AgentExecutionEntity> AgentExecutions => Set<AgentExecutionEntity>();
    public DbSet<ReviewEventEntity> ReviewEvents => Set<ReviewEventEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReviewRunEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Pattern).HasMaxLength(32);
            entity.Property(x => x.Status).HasMaxLength(32);
            entity.HasMany(x => x.AgentExecutions).WithOne(x => x.ReviewRun).HasForeignKey(x => x.ReviewRunId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Events).WithOne(x => x.ReviewRun).HasForeignKey(x => x.ReviewRunId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<AgentExecutionEntity>().HasIndex(x => new { x.ReviewRunId, x.Sequence }).IsUnique();
        modelBuilder.Entity<ReviewEventEntity>().HasIndex(x => new { x.ReviewRunId, x.Sequence }).IsUnique();
    }
}
