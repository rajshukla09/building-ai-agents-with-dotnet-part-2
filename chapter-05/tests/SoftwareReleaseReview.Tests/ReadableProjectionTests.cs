using SoftwareReleaseReview.Api.Contracts;
using SoftwareReleaseReview.Api.Orchestrations;
using Xunit;

namespace SoftwareReleaseReview.Tests;

public sealed class ReadableProjectionTests
{
    [Fact]
    public void Projection_joins_chunks_and_hides_framework_executors_and_stream_events()
    {
        var start = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        ReviewEvent[] raw =
        [
            Event("ExecutorInvokedEvent", "Start", null, 0),
            Event("AgentResponseUpdateEvent", "SecurityAgent_abc123", "risk ", 1),
            Event("AgentResponseUpdateEvent", "SecurityAgent_abc123", "found", 2),
            Event("ExecutorCompletedEvent", "SecurityAgent_abc123", null, 3),
            Event("AgentResponseUpdateEvent", "ReleaseAgent_987", "DEC", 4),
            Event("AgentResponseUpdateEvent", "ReleaseAgent_987", "ISION: HOLD", 5),
            Event("ExecutorCompletedEvent", "ReleaseAgent_987", null, 6),
            Event("OutputMessagesEvent", "OutputMessages", "raw transcript", 7)
        ];

        var readable = ReadableEventProjector.Project("Sequential", raw);

        Assert.DoesNotContain(readable, x => x.RuntimeType.Contains("ResponseUpdate"));
        Assert.DoesNotContain(readable, x => x.Executor is "Start" or "OutputMessages");
        Assert.Contains(readable, x => x.Executor == "SecurityAgent" && x.Text == "risk found");
        Assert.Contains(readable, x => x.Executor == "ReleaseAgent" && x.Text == "DECISION: HOLD");
        Assert.Equal("WorkflowStarted", readable.First().RuntimeType);
        Assert.Equal("WorkflowCompleted", readable.Last().RuntimeType);

        ReviewEvent Event(string type, string? executor, string? text, int milliseconds) =>
            new(type, executor, text, start.AddMilliseconds(milliseconds));
    }
}
