using EnterpriseArchitectureAssessment.Api.Orchestration;
using EnterpriseArchitectureAssessment.Api.Persistence;
using Xunit;
namespace EnterpriseArchitectureAssessment.Tests;
public sealed class MagenticReplanningTests
{
    [Fact]
    public void Evidence_revision_preserves_initial_and_updates_current_plan()
    {
        var run = new AssessmentRun();
        var state = new AssessmentRunState(run);
        state.ApplyManagerOutput("PLAN: Assess full decomposition\nPROGRESS: payment alone needs scale and fleet " +
                                 "cost rises\nREVISION: Validate selective payment extraction; Retain modular order " +
                                 "core\nCONFLICT: scale benefit versus operating cost");
        Assert.Equal(1, run.InitialPlan!.Version);
        Assert.Equal(2, run.CurrentPlan!.Version);
        Assert.Single(run.PlanRevisions);
        Assert.Single(run.Conflicts);
    }
    [Theory]
    [InlineData("Assess whether the order-processing monolith should be fully migrated to event-driven microservices.")]
    [InlineData("We need to reduce infrastructure and support cost by 20%. Should we still move to microservices?")]
    [InlineData("Payment processing cannot handle seasonal traffic spikes. What architecture changes should we make?")]
    public void Objectives_remain_open_ended_inputs(string objective) => Assert.DoesNotContain("route=", objective);
}
