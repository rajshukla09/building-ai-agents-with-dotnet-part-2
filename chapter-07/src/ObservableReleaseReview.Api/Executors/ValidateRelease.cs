using Microsoft.Agents.AI.Workflows;
using ObservableReleaseReview.Api.Contracts;

namespace ObservableReleaseReview.Api.Executors;

internal sealed class ValidateRelease()
    : Executor<StartReview, ValidatedRelease>(nameof(ValidateRelease), ExecutorOptions.Default, true)
{
    public override ValueTask<ValidatedRelease> HandleAsync(
        StartReview input,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input.Release))
        {
            throw new ArgumentException("Release is required.");
        }

        var highRisk = input.Release.Contains("authentication", StringComparison.OrdinalIgnoreCase)
            || input.Release.Contains("payment", StringComparison.OrdinalIgnoreCase);

        return ValueTask.FromResult(new ValidatedRelease(
            input.RunId,
            input.Release,
            input.SimulateFailure,
            highRisk));
    }
}
