using EnterpriseArchitectureAssessment.Api.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseArchitectureAssessment.Api.Persistence;

public sealed class AssessmentDbContext(DbContextOptions<AssessmentDbContext> options) : DbContext(options)
{
    public DbSet<AssessmentRunEntity> AssessmentRuns => Set<AssessmentRunEntity>();
    public DbSet<PlanRevisionEntity> PlanRevisions => Set<PlanRevisionEntity>();
    public DbSet<AgentInvocationEntity> AgentInvocations => Set<AgentInvocationEntity>();
    public DbSet<ToolInvocationEntity> ToolInvocations => Set<ToolInvocationEntity>();
    public DbSet<AssessmentEventEntity> AssessmentEvents => Set<AssessmentEventEntity>();
    public DbSet<ProgressEvaluationEntity> ProgressEvaluations => Set<ProgressEvaluationEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AssessmentRunEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Objective).IsRequired();
            entity.HasIndex(x => x.StartedAt);
            entity.HasMany(x => x.PlanRevisions).WithOne().HasForeignKey(x => x.AssessmentRunId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.AgentInvocations).WithOne().HasForeignKey(x => x.AssessmentRunId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.ToolInvocations).WithOne().HasForeignKey(x => x.AssessmentRunId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Events).WithOne().HasForeignKey(x => x.AssessmentRunId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.ProgressEvaluations).WithOne().HasForeignKey(x => x.AssessmentRunId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<PlanRevisionEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<AgentInvocationEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<ToolInvocationEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<AssessmentEventEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<ProgressEvaluationEntity>().HasKey(x => x.Id);
    }
}
