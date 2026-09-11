using EnterpriseArchitectureAssessment.Api.Configuration;
using EnterpriseArchitectureAssessment.Api.Contracts;
using EnterpriseArchitectureAssessment.Api.Orchestration;
using EnterpriseArchitectureAssessment.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace EnterpriseArchitectureAssessment.Tests;

public sealed class AssessmentPersistenceTests
{
    [Fact]
    public void Run_and_children_survive_context_recreation_with_configuration_snapshot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"assessment-{Guid.NewGuid()}.db");
        try
        {
            Guid id;
            using (var db = Context(path))
            {
                db.Database.Migrate();
                var store = Store(db);
                var run = store.Create(Guid.NewGuid(), "objective");
                id = run.Id;
                var invocationId = Guid.NewGuid();
                run.AgentInvocations.Add(new(invocationId, "CostAgent", "Compare operating cost", run.StartedAt,
                    run.StartedAt.AddSeconds(2), "Fleet cost increases."));
                run.ToolInvocations.Add(new(invocationId, "cost", "estimate_target_architecture_cost", "{}", false,
                    run.StartedAt.AddSeconds(1), 25, true, Agent: "CostAgent"));
                run.PlanRevisions.Add(new(1, 2, "Cost evidence", "Prefer selective extraction", run.StartedAt));
                run.ProgressEvaluations.Add("Cost evidence collected");
                run.Events.Add(new(1, AssessmentEventTypes.AgentCompleted, run.StartedAt, "Completed", "CostAgent"));
                store.Save(run);
            }

            using (var recreated = Context(path))
            {
                var loaded = Store(recreated).Get(id)!;
                Assert.Equal("gpt-test", loaded.Configuration.ModelDeployment);
                Assert.Equal(12, loaded.Configuration.MaxTurns);
                Assert.Single(loaded.AgentInvocations);
                Assert.Single(loaded.ToolInvocations);
                Assert.Single(loaded.PlanRevisions);
                Assert.Single(loaded.ProgressEvaluations);
                Assert.Single(loaded.Events);
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void History_is_newest_first_and_comparison_is_data_driven()
    {
        using var db = Context(":memory:");
        db.Database.OpenConnection();
        db.Database.Migrate();
        var store = Store(db);
        var older = store.Create(Guid.NewGuid(), "same objective");
        older.StartedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        older.DurationMilliseconds = 52_000;
        older.AgentInvocations.Add(new(Guid.NewGuid(), "CostAgent", "cost", older.StartedAt));
        older.ToolInvocations.Add(new(Guid.NewGuid(), "cost", "get_cost", "{}", true, older.StartedAt));
        store.Save(older);
        var newer = store.Create(Guid.NewGuid(), "same objective");
        newer.DurationMilliseconds = 41_000;
        newer.PlanRevisions.Add(new(1, 2, "evidence", "change", newer.StartedAt));
        store.Save(newer);

        Assert.Equal(newer.Id, store.List()[0].Id);
        var comparison = AssessmentComparisonService.Compare(older, newer);
        Assert.Equal(52_000, comparison.Left.DurationMilliseconds);
        Assert.Equal(1, comparison.Left.AgentInvocations);
        Assert.Equal(1, comparison.Left.CacheHits);
        Assert.Equal(1, comparison.Right.PlanRevisions);
    }

    private static AssessmentDbContext Context(string path) => new(new DbContextOptionsBuilder<AssessmentDbContext>()
        .UseSqlite(path == ":memory:" ? "Data Source=:memory:" : $"Data Source={path};Pooling=False").Options);

    private static AssessmentRunRepository Store(AssessmentDbContext db) => new(db,
        Options.Create(new MagenticAssessmentOptions { MaxTurns = 12, MaxAgentInvocations = 10, MaxToolCallsPerAgent = 5 }),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["AzureOpenAI:DeploymentName"] = "gpt-test"
        }).Build());
}
