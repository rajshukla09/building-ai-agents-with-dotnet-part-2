using Microsoft.Agents.AI;

namespace SoftwareReleaseReview.Api.Agents;

public sealed class SecurityAgent(ReleaseReviewAgentFactory factory)
{
    public AIAgent Create() => factory.Create(
        "SecurityAgent",
        "Reviews release security and authentication risk.",
        "Review authentication, authorization, secrets, and payment-security risk. Treat the supplied release facts as mocked evidence.");
}
