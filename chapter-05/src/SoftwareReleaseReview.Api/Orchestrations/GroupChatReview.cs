using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Specialized;
using SoftwareReleaseReview.Api.Agents;
using SoftwareReleaseReview.Api.Contracts;

namespace SoftwareReleaseReview.Api.Orchestrations;

public sealed class GroupChatReview(SecurityAgent security, QualityAgent quality,
    ArchitectureAgent architecture) : IReleaseReview
{
    internal const int MaximumTurns = 6;

    public async Task<ReviewResponse> RunAsync(string releaseRequest, CancellationToken cancellationToken)
    {
        AIAgent[] participants = [security.Create(), quality.Create(), architecture.Create()];

        // The installed API takes a manager factory first. MAF supplies the final participant list
        // to the factory after AddParticipants has populated the native group-chat builder.
        Workflow workflow = AgentWorkflowBuilder
            .CreateGroupChatBuilderWith(agents => new RoundRobinGroupChatManager(agents)
            {
                MaximumIterationCount = MaximumTurns
            })
            .AddParticipants(participants)
            .Build();
        var prompt = $"{releaseRequest}\nDiscuss, challenge earlier findings, and finish with a shared release recommendation.";
        var execution = await InProcessExecution.RunAsync(workflow, prompt, cancellationToken: cancellationToken);

        return WorkflowResultReader.Read("Group Chat", execution.NewEvents);
    }
}
