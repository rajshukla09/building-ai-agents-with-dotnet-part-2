using Microsoft.Agents.AI.Workflows;
using ObservableReleaseReview.Api.Contracts;

namespace ObservableReleaseReview.Api.Executors;

internal abstract class ReviewExecutor(string id, string area)
    : Executor<ValidatedRelease, ReviewFinding>(id, ExecutorOptions.Default, true)
{
    public override ValueTask<ReviewFinding> HandleAsync(
        ValidatedRelease input,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {

        if (input.SimulateFailure && area == "Security")
        {
            throw new InvalidOperationException("Simulated security review failure.");
        }
        return ValueTask.FromResult(new ReviewFinding(
            input.RunId,
            area,
            $"{area} review passed",
            input.HighRisk));
    }
}
