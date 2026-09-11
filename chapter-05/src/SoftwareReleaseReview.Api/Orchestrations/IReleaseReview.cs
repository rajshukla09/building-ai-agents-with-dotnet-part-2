using SoftwareReleaseReview.Api.Contracts;

namespace SoftwareReleaseReview.Api.Orchestrations;

public interface IReleaseReview
{
    Task<ReviewResponse> RunAsync(string release, CancellationToken cancellationToken);
}
