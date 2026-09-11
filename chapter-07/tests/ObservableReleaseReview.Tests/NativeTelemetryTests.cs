using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Agents.AI.Workflows.Observability;
using Microsoft.Extensions.Options;
using ObservableReleaseReview.Api.Contracts;
using ObservableReleaseReview.Api.Workflows;
using Xunit;

namespace ObservableReleaseReview.Tests;

public sealed class NativeTelemetryTests
{
    [Fact]
    public async Task Native_maf_spans_share_the_returned_trace_id()
    {
        var activities = new ConcurrentBag<Activity>();
        using var listener = Listen(activities);
        using var parent = new Activity("test-http-request").Start();

        var result = await Create().ReviewAsync(new ReleaseReviewRequest(
            "Release 4.8 contains authentication and payment changes.",
            false));

        parent.Stop();
        var mafActivities = activities
            .Where(activity => activity.Source.Name == "Microsoft.Agents.AI.Workflows")
            .Where(activity => activity.TraceId.ToString() == result.RunId)
            .ToArray();

        Assert.NotEmpty(mafActivities);
        Assert.Equal(parent.TraceId.ToString(), result.RunId);
        Assert.Contains(mafActivities, activity => activity.DisplayName.Contains("workflow", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(mafActivities, activity => activity.DisplayName.Contains("executor", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(mafActivities, activity => activity.DisplayName.Contains("edge", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(mafActivities, activity => activity.DisplayName.Contains("message", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(mafActivities, activity => activity.DisplayName == "executor.process ExtraReview");
        Assert.Contains(mafActivities, activity => activity.DisplayName == "executor.process SecurityReview");
        Assert.Contains(mafActivities, activity => activity.DisplayName == "executor.process QualityReview");
        Assert.Contains(mafActivities, activity => activity.DisplayName == "executor.process ArchitectureReview");
        Assert.Contains(mafActivities, activity => activity.DisplayName == "executor.process ReleaseDecision");
        Assert.Contains(mafActivities, activity => activity.Tags.Any(tag =>
            tag.Key == "edge_group.delivery_status" && tag.Value == "buffered"));
        Assert.DoesNotContain(mafActivities.SelectMany(activity => activity.Tags), tag =>
            tag.Value?.Contains("authentication and payment", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task Native_failure_trace_stops_at_failed_executor()
    {
        var activities = new ConcurrentBag<Activity>();
        using var listener = Listen(activities);
        using var parent = new Activity("test-http-request").Start();

        var result = await Create().ReviewAsync(new ReleaseReviewRequest(
            "Release 4.8 contains authentication and payment changes.",
            true));

        parent.Stop();
        var mafActivities = activities
            .Where(activity => activity.Source.Name == "Microsoft.Agents.AI.Workflows")
            .Where(activity => activity.TraceId.ToString() == result.RunId)
            .ToArray();

        Assert.Equal("Failed", result.Status);
        Assert.Contains(mafActivities, activity =>
            activity.DisplayName == "executor.process SecurityReview");
        Assert.DoesNotContain(mafActivities, activity =>
            activity.DisplayName == "executor.process ReleaseDecision");
    }

    private static ReleaseReviewWorkflow Create() => new(
        Options.Create(new WorkflowTelemetryOptions
        {
            EnableSensitiveData = false
        }));

    private static ActivityListener Listen(ConcurrentBag<Activity> activities)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Microsoft.Agents.AI.Workflows",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Add
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
