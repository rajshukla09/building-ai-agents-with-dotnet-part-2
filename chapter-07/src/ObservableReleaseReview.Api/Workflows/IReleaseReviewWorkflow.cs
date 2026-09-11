using ObservableReleaseReview.Api.Contracts;

namespace ObservableReleaseReview.Api.Workflows;

public interface IReleaseReviewWorkflow
{
    bool SensitiveDataEnabled { get; }

    Task<ReleaseReviewResponse> ReviewAsync(
        ReleaseReviewRequest request,
        CancellationToken cancellationToken = default);
}
