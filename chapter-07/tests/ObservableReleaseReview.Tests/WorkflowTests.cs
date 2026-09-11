using Microsoft.Agents.AI.Workflows.Observability;
using Microsoft.Extensions.Options;
using ObservableReleaseReview.Api.Contracts;
using ObservableReleaseReview.Api.Workflows;
using Xunit;

namespace ObservableReleaseReview.Tests;

public sealed class WorkflowTests
{
    private static ReleaseReviewWorkflow Create(bool sensitive = false) => new(Options.Create(new WorkflowTelemetryOptions { EnableSensitiveData = sensitive }));

    [Fact] public void Sensitive_data_is_disabled_by_default() => Assert.False(Create().SensitiveDataEnabled);
    [Fact] public void Telemetry_configuration_is_applied() => Assert.True(Create(true).SensitiveDataEnabled);

    [Fact] public async Task Normal_workflow_completes_and_fan_in_aggregates()
    {
        var result = await Create().ReviewAsync(new("Release 4.8 contains documentation changes."));
        Assert.True(result.Status == "Completed", result.FinalDecision); Assert.Equal("Safe to deploy", result.FinalDecision); Assert.False(string.IsNullOrWhiteSpace(result.RunId));
        Assert.False(result.HighRisk);
        Assert.Equal("Skipped", result.Executors.Single(item => item.Name == "ExtraReview").Status);
        Assert.All(
            result.Executors.Where(item => item.Name is "SecurityReview" or "QualityReview" or "ArchitectureReview"),
            item => Assert.Equal("Completed", item.Status));
    }

    [Fact] public async Task High_risk_conditional_path_completes()
    {
        var result = await Create().ReviewAsync(new("Release 4.8 contains authentication and payment changes."));
        Assert.True(result.Status == "Completed", result.FinalDecision); Assert.Equal("Deploy after high-risk review", result.FinalDecision);
        Assert.Equal("Completed", result.Executors.Single(item => item.Name == "ExtraReview").Status);
    }

    [Fact] public async Task Failure_path_reports_failure_and_no_decision()
    {
        var result = await Create().ReviewAsync(new("Release 4.8", true));
        Assert.Equal("Failed", result.Status); Assert.Null(result.FinalDecision);
        Assert.Equal("Failed", result.Executors.Single(item => item.Name == "SecurityReview").Status);
        Assert.Equal("Not Executed", result.Executors.Single(item => item.Name == "ReleaseDecision").Status);
    }
}
