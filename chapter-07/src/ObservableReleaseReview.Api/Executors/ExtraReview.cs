using Microsoft.Agents.AI.Workflows;
using ObservableReleaseReview.Api.Contracts;

namespace ObservableReleaseReview.Api.Executors;

internal sealed class ExtraReview()
    : Executor<ValidatedRelease, ValidatedRelease>(nameof(ExtraReview), ExecutorOptions.Default, true)
{
    public override ValueTask<ValidatedRelease> HandleAsync(
        ValidatedRelease input,
        IWorkflowContext context,
        CancellationToken cancellationToken = default) => ValueTask.FromResult(input);
}
