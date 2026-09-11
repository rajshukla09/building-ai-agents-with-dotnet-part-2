using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using SoftwareReleaseReview.Api.Agents;
using SoftwareReleaseReview.Api.Contracts;

namespace SoftwareReleaseReview.Api.Orchestrations;

public sealed class ConcurrentReview(SecurityAgent security, QualityAgent quality,
    ArchitectureAgent architecture, ReleaseAgent release) : IReleaseReview
{
    public async Task<ReviewResponse> RunAsync(string releaseRequest, CancellationToken cancellationToken)
    {
        AIAgent[] independentReviewers = [security.Create(), quality.Create(), architecture.Create()];

        // BuildConcurrent performs native fan-out and its workflow output is the fan-in collection.
        Workflow fanOutFanIn = AgentWorkflowBuilder.BuildConcurrent(independentReviewers);
        var reviews = await InProcessExecution.RunAsync(
            fanOutFanIn, releaseRequest, cancellationToken: cancellationToken);
        var reviewResult = WorkflowResultReader.Read("Concurrent reviews", reviews.NewEvents);

        // The release agent receives only the collected independent findings, never another reviewer's live context.
        Workflow decision = AgentWorkflowBuilder.BuildSequential(release.Create());
        var decisionInput = $"Release request:\n{releaseRequest}\n\nIndependent findings (fan-in):\n{reviewResult.Decision}";
        var releaseDecision = await InProcessExecution.RunAsync(
            decision, decisionInput, cancellationToken: cancellationToken);
        var result = WorkflowResultReader.Read("Concurrent", releaseDecision.NewEvents);

        return result with
        {
            Events = [.. reviewResult.Events, .. result.Events],
            RawEvents = [.. reviewResult.RawEvents, .. result.RawEvents]
        };
    }
}
