using Microsoft.Agents.AI;

namespace SoftwareReleaseReview.Api.Agents;

public sealed class ReleaseAgent(ReleaseReviewAgentFactory factory)
{
    public AIAgent Create() => factory.Create(
        "ReleaseAgent",
        "Makes the final deploy, hold, or conditional-deploy decision.",
        "Reconcile the available specialist findings and finish with DECISION: DEPLOY, CONDITIONAL DEPLOY, or HOLD.");

    public AIAgent CreateHandoffEntryAgent() => factory.Create(
        "ReleaseAgent",
        "Routes release concerns directly to the best specialist.",
        "Start the review. Transfer to SecurityAgent for authentication, authorization, or payment risk; otherwise decide the release. After specialists report, issue the final decision.");
}
