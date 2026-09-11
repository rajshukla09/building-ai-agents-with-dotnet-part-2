using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using SoftwareReleaseReview.Api.Agents;
using SoftwareReleaseReview.Api.Contracts;

namespace SoftwareReleaseReview.Api.Orchestrations;

public sealed class SequentialReview(SecurityAgent security, QualityAgent quality,
    ArchitectureAgent architecture, ReleaseAgent release) : IReleaseReview
{
    public async Task<ReviewResponse> RunAsync(string releaseRequest, CancellationToken cancellationToken)
    {
        AIAgent[] agents = [security.Create(), quality.Create(), architecture.Create(), release.Create()];

        Workflow workflow = AgentWorkflowBuilder.BuildSequential(agents);
        var execution = await InProcessExecution.RunAsync(workflow, releaseRequest, cancellationToken: cancellationToken);

        return WorkflowResultReader.Read("Sequential", execution.NewEvents);
    }
}
