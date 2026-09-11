using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using SoftwareReleaseReview.Api.Agents;
using SoftwareReleaseReview.Api.Contracts;

namespace SoftwareReleaseReview.Api.Orchestrations;

public sealed class HandoffReview(ReleaseAgent release, SecurityAgent security,
    ArchitectureAgent architecture) : IReleaseReview
{
    public async Task<ReviewResponse> RunAsync(string releaseRequest, CancellationToken cancellationToken)
    {
        AIAgent releaseAgent = release.CreateHandoffEntryAgent();
        AIAgent securityAgent = security.Create();
        AIAgent architectureAgent = architecture.Create();

        // Specialists transfer control directly; there is no application supervisor loop.
        Workflow workflow = AgentWorkflowBuilder.CreateHandoffBuilderWith(releaseAgent)
            .WithHandoff(releaseAgent, securityAgent, "Authentication, authorization, secrets, or payment-security concern")
            .WithHandoff(securityAgent, architectureAgent, "The security finding depends on boundaries, dependencies, or rollback design")
            .WithHandoff(securityAgent, releaseAgent, "Security review is complete and a release decision is needed")
            .WithHandoff(architectureAgent, releaseAgent, "Architecture review is complete and a release decision is needed")
            .Build();
        var execution = await InProcessExecution.RunAsync(workflow, releaseRequest, cancellationToken: cancellationToken);

        return WorkflowResultReader.Read("Handoff", execution.NewEvents);
    }
}
