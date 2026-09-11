using Microsoft.Agents.AI;

namespace SoftwareReleaseReview.Api.Agents;

public sealed class QualityAgent(ReleaseReviewAgentFactory factory)
{
    public AIAgent Create() => factory.Create(
        "QualityAgent",
        "Reviews test coverage, regressions, and release quality.",
        "Review test evidence, rollback readiness, and regression risk. In a sequence, explicitly consider earlier findings.");
}
