using EnterpriseArchitectureAssessment.Api.Contracts;
using EnterpriseArchitectureAssessment.Api.Persistence;

namespace EnterpriseArchitectureAssessment.Api.Orchestration;

public static class AssessmentComparisonService
{
    public static AssessmentRunComparison Compare(AssessmentRun left, AssessmentRun right) =>
        new(Metrics(left), Metrics(right));

    public static AssessmentRunMetrics Metrics(AssessmentRun run)
    {
        var knownAgents = run.AgentInvocations.Select(x => x.Agent).Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal).Order().ToArray();
        var callsPerAgent = run.ToolInvocations.Where(x => !string.IsNullOrWhiteSpace(x.Agent))
            .GroupBy(x => x.Agent!, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
        return new(run.Id, run.Objective, run.Status, run.StartedAt, run.DurationMilliseconds,
            run.AgentInvocations.Count, knownAgents, run.ToolInvocations.Count,
            run.ToolInvocations.Select(x => $"{x.Server}/{x.Tool}").Distinct(StringComparer.Ordinal).Order().ToArray(),
            callsPerAgent, run.PlanRevisions.Count, run.ToolInvocations.Count(x => !x.Success),
            run.ToolInvocations.Count(x => x.CacheHit), run.LimitReached,
            run.FinalRecommendation?.Length ?? 0, run.InitialPlan, run.CurrentPlan,
            run.CompletionReason, run.FinalRecommendation, run.Configuration);
    }
}
