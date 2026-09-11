using Microsoft.EntityFrameworkCore;
using SoftwareReleaseReview.Api.Persistence;
using SoftwareReleaseReview.Api.Persistence.Entities;
using SoftwareReleaseReview.Api.Persistence.Repositories;
using SoftwareReleaseReview.Api.Services;
using Xunit;

namespace SoftwareReleaseReview.Tests;

public sealed class PersistenceAndComparisonTests
{
    [Fact]
    public async Task Run_graph_survives_dbcontext_recreation_with_ordered_children()
    {
        var path = Path.Combine(Path.GetTempPath(), $"review-{Guid.NewGuid():N}.db");
        try
        {
            await using (var first = CreateContext(path))
            {
                await first.Database.MigrateAsync();
                await new ReviewRunRepository(first).AddAsync(SampleRun("Sequential"), default);
            }

            await using (var second = CreateContext(path))
            {
                var persisted = Assert.Single(await new ReviewRunRepository(second).ListAsync(default));
                Assert.True(persisted.Success);
                Assert.Equal([1, 2], persisted.AgentExecutions.OrderBy(x => x.Sequence).Select(x => x.Sequence).ToArray());
                Assert.Equal([1, 2], persisted.Events.OrderBy(x => x.Sequence).Select(x => x.Sequence).ToArray());
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Concurrent_metrics_preserve_branch_timing_and_prove_overlap()
    {
        var run = SampleRun("Concurrent");
        var start = run.StartedAt;
        run.AgentExecutions.AddRange(
        [
            new() { Sequence = 3, AgentName = "ArchitectureAgent_aabbcc", StartedAt = start.AddMilliseconds(5), CompletedAt = start.AddMilliseconds(80), DurationMilliseconds = 75, Status = "Completed" },
            new() { Sequence = 4, AgentName = "ReleaseAgent_123", StartedAt = start.AddMilliseconds(101), CompletedAt = start.AddMilliseconds(119), DurationMilliseconds = 18, Status = "Completed", Finding = "DECISION: CONDITIONAL DEPLOY" },
            new() { Sequence = 5, AgentName = "Batcher/Concurrent", StartedAt = start, CompletedAt = start.AddMilliseconds(110), DurationMilliseconds = 110, Status = "Completed" }
        ]);
        var metrics = RunComparisonService.ToMetrics(run);

        Assert.Equal(3, metrics.ParallelBranches);
        Assert.Equal(4, metrics.AgentExecutions);
        Assert.Equal("SecurityAgent", metrics.SlowestBranch);
        Assert.True(metrics.ConcurrentTiming!.ExecutionsOverlap);
        Assert.Equal(3, metrics.ConcurrentTiming.Branches.Count);
        Assert.Equal(["Fan-Out", "SecurityAgent | QualityAgent | ArchitectureAgent", "Fan-In", "ReleaseAgent"], metrics.ExecutionOrder.ToArray());
        Assert.Equal("DECISION: CONDITIONAL DEPLOY", metrics.FinalDecision);
    }

    [Theory]
    [InlineData("SecurityAgent_7fba578c42054ec18a1cfe7d63344a72", "SecurityAgent")]
    [InlineData("QualityAgent", "QualityAgent")]
    [InlineData("Start", null)]
    [InlineData("Batcher/Concurrent", null)]
    [InlineData("ConcurrentEnd", null)]
    [InlineData("OutputMessages", null)]
    public void Business_agent_names_normalize_generated_ids_and_remove_infrastructure(string input, string? expected) =>
        Assert.Equal(expected, BusinessAgentNames.Normalize(input));

    [Fact]
    public void Pattern_metrics_reconstruct_handoffs_and_group_chat_turns()
    {
        var handoff = SampleRun("Handoff");
        handoff.Events.Add(Event(3, "HandoffEvent", "SecurityAgent", 15));
        var handoffMetrics = RunComparisonService.ToMetrics(handoff);
        Assert.Equal(1, handoffMetrics.Handoffs);
        Assert.Equal(["SecurityAgent", "QualityAgent"], handoffMetrics.HandoffPath.ToArray());

        var chat = SampleRun("GroupChat");
        chat.Events.Add(Event(3, "TurnCompletedEvent", "SecurityAgent", 15));
        var chatMetrics = RunComparisonService.ToMetrics(chat);
        Assert.Equal(1, chatMetrics.ConversationTurns);
        Assert.Equal(3, chatMetrics.Contributions!["SecurityAgent"]);
    }

    private static ReviewDbContext CreateContext(string path) => new(new DbContextOptionsBuilder<ReviewDbContext>()
        .UseSqlite($"Data Source={path};Pooling=False").Options);

    private static ReviewRunEntity SampleRun(string pattern)
    {
        var start = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        return new ReviewRunEntity
        {
            Id = Guid.NewGuid(), Pattern = pattern, ReleaseRequest = "Release 4.8", Status = "Completed",
            StartedAt = start, CompletedAt = start.AddMilliseconds(120), DurationMilliseconds = 120,
            FinalDecision = "CONDITIONAL DEPLOY", Success = true,
            AgentExecutions =
            [
                new() { Sequence = 1, AgentName = "SecurityAgent", StartedAt = start, CompletedAt = start.AddMilliseconds(100), DurationMilliseconds = 100, Status = "Completed" },
                new() { Sequence = 2, AgentName = "QualityAgent", StartedAt = start.AddMilliseconds(10), CompletedAt = start.AddMilliseconds(70), DurationMilliseconds = 60, Status = "Completed" }
            ],
            Events = [Event(1, "AgentStartedEvent", "SecurityAgent", 0), Event(2, "AgentCompletedEvent", "SecurityAgent", 100)]
        };
    }

    private static ReviewEventEntity Event(int sequence, string type, string agent, int milliseconds) => new()
    {
        Sequence = sequence, EventType = type, Agent = agent,
        Timestamp = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero).AddMilliseconds(milliseconds)
    };
}
