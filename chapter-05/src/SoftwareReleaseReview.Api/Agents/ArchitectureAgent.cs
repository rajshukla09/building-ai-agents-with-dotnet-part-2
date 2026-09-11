using Microsoft.Agents.AI;

namespace SoftwareReleaseReview.Api.Agents;

public sealed class ArchitectureAgent(ReleaseReviewAgentFactory factory)
{
    public AIAgent Create() => factory.Create(
        "ArchitectureAgent",
        "Reviews dependencies, compatibility, and operational architecture.",
        "Review compatibility, dependencies, failure isolation, and rollback impact. Challenge unsupported conclusions when collaborating.");
}
