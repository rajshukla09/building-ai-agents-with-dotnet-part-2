using EnterpriseArchitectureAssessment.Api.Contracts;
using EnterpriseArchitectureAssessment.Api.Orchestration;
using EnterpriseArchitectureAssessment.Api.Persistence;
using Xunit;
namespace EnterpriseArchitectureAssessment.Tests;
public sealed class MagenticPlanningTests
{
    [Fact]
    public void Manager_output_projects_the_real_plan_and_progress()
    {
        var run = new AssessmentRun();
        var state = new AssessmentRunState(run);
        state.ApplyManagerOutput("PLAN: Inspect scale; Compare cost\nDELEGATION: ArchitectureAgent — inspect payment " +
                                 "boundary\nPROGRESS: scale evidence collected");
        Assert.Equal(1, run.InitialPlan!.Version);
        Assert.Equal(2, run.CurrentPlan!.Items.Count);
        Assert.Single(run.AgentInvocations);
        Assert.Single(run.ProgressEvaluations);
    }
    [Fact]
    public void Native_MAF_entry_point_is_used_without_an_application_loop()
    {
        var source = File.ReadAllText(
            Source("src/EnterpriseArchitectureAssessment.Api/Orchestration/MagenticAssessmentOrchestrator.cs"));
        Assert.Contains("new MagenticWorkflowBuilder", source);
        Assert.Contains(".AddParticipants(participants)", source);
        Assert.Contains("InProcessExecution.RunAsync", source);
        Assert.DoesNotContain("while (", source);
    }

    [Fact]
    public void Native_event_projection_uses_observed_participant_data_and_deduplicates_manager_output()
    {
        var run = new AssessmentRun();
        var state = new AssessmentRunState(run);
        state.ApplyNativeResult(new NativeMagenticResult(
            "COMPLETE: Keep the modular core and isolate payments.",
            [
                new("ExecutorInvokedEvent", "ArchitectureAgent", "Inspect service boundaries"),
                new("ExecutorCompletedEvent", "ArchitectureAgent", "Payment scaling is independently constrained."),
                new("WorkflowOutputEvent", null, "PLAN: Inspect boundaries; Validate payment load\nPROGRESS: Evidence collected"),
                new("WorkflowOutputEvent", null, "PLAN: Inspect boundaries; Validate payment load\nPROGRESS: Evidence collected")
            ]));

        var invocation = Assert.Single(run.AgentInvocations);
        Assert.NotNull(invocation.CompletedAt);
        Assert.Equal("Payment scaling is independently constrained.", invocation.Result);
        Assert.Single(run.ProgressEvaluations);
        Assert.Equal(2, run.CurrentPlan!.Items.Count);
        Assert.Contains(run.Events, x => x.Type == AssessmentEventTypes.AgentStarted);
        Assert.Contains(run.Events, x => x.Type == AssessmentEventTypes.AgentCompleted);
    }

    [Fact]
    public void Missing_native_artifacts_do_not_create_placeholder_state()
    {
        var run = new AssessmentRun();
        new AssessmentRunState(run).ApplyNativeResult(new NativeMagenticResult(null,
            [new("WorkflowOutputEvent", null, null)]));

        Assert.Null(run.InitialPlan);
        Assert.Empty(run.AgentInvocations);
        Assert.Empty(run.ProgressEvaluations);
        Assert.DoesNotContain(run.Events, x => x.Type is AssessmentEventTypes.PlanCreated or AssessmentEventTypes.AgentCompleted);
    }
    private static string Source(string relative) => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                                                                                   "../../../../../", relative));
}
