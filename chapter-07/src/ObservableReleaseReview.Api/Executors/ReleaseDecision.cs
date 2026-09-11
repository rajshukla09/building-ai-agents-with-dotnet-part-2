using System.Collections.Concurrent;
using Microsoft.Agents.AI.Workflows;
using ObservableReleaseReview.Api.Contracts;

namespace ObservableReleaseReview.Api.Executors;

internal sealed class ReleaseDecision(
    ConcurrentDictionary<string, ConcurrentBag<ReviewFinding>> findings)
    : Executor<ReviewFinding, ReleaseDecisionResult>(nameof(ReleaseDecision), ExecutorOptions.Default, true)
{
    public override ValueTask<ReleaseDecisionResult> HandleAsync(
        ReviewFinding input,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var collected = findings.GetOrAdd(input.RunId, _ => []);
        collected.Add(input);
        var count = collected.Count;
        var decision = count == 3
            ? input.HighRisk ? "Deploy after high-risk review" : "Safe to deploy"
            : $"Aggregating ({count}/3)";

        return ValueTask.FromResult(new ReleaseDecisionResult(input.RunId, decision));
    }
}
