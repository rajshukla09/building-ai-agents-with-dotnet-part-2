using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace SoftwareReleaseReview.Api.Persistence.Migrations;

[DbContext(typeof(ReviewDbContext))]
partial class ReviewDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "9.0.4");
        modelBuilder.Entity("SoftwareReleaseReview.Api.Persistence.Entities.ReviewRunEntity", b =>
        {
            b.Property<Guid>("Id");
            b.Property<DateTimeOffset?>("CompletedAt");
            b.Property<long>("DurationMilliseconds");
            b.Property<string>("Error");
            b.Property<string>("FinalDecision");
            b.Property<string>("Pattern").IsRequired().HasMaxLength(32);
            b.Property<string>("ReleaseRequest").IsRequired();
            b.Property<DateTimeOffset>("StartedAt");
            b.Property<string>("Status").IsRequired().HasMaxLength(32);
            b.Property<bool>("Success");
            b.HasKey("Id");
            b.ToTable("ReviewRuns");
        });
        modelBuilder.Entity("SoftwareReleaseReview.Api.Persistence.Entities.AgentExecutionEntity", b =>
        {
            b.Property<long>("Id").ValueGeneratedOnAdd();
            b.Property<string>("AgentName").IsRequired();
            b.Property<DateTimeOffset>("CompletedAt");
            b.Property<long>("DurationMilliseconds");
            b.Property<string>("Finding");
            b.Property<Guid>("ReviewRunId");
            b.Property<int>("Sequence");
            b.Property<DateTimeOffset>("StartedAt");
            b.Property<string>("Status").IsRequired();
            b.HasKey("Id");
            b.HasIndex("ReviewRunId", "Sequence").IsUnique();
            b.ToTable("AgentExecutions");
        });
        modelBuilder.Entity("SoftwareReleaseReview.Api.Persistence.Entities.ReviewEventEntity", b =>
        {
            b.Property<long>("Id").ValueGeneratedOnAdd();
            b.Property<string>("Agent");
            b.Property<string>("EventType").IsRequired();
            b.Property<Guid>("ReviewRunId");
            b.Property<int>("Sequence");
            b.Property<string>("Summary");
            b.Property<DateTimeOffset>("Timestamp");
            b.HasKey("Id");
            b.HasIndex("ReviewRunId", "Sequence").IsUnique();
            b.ToTable("ReviewEvents");
        });
        modelBuilder.Entity(
            "SoftwareReleaseReview.Api.Persistence.Entities.AgentExecutionEntity",
            b => b
                .HasOne("SoftwareReleaseReview.Api.Persistence.Entities.ReviewRunEntity", "ReviewRun")
                .WithMany("AgentExecutions")
                .HasForeignKey("ReviewRunId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired());
        modelBuilder.Entity(
            "SoftwareReleaseReview.Api.Persistence.Entities.ReviewEventEntity",
            b => b
                .HasOne("SoftwareReleaseReview.Api.Persistence.Entities.ReviewRunEntity", "ReviewRun")
                .WithMany("Events")
                .HasForeignKey("ReviewRunId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired());
    }
}
