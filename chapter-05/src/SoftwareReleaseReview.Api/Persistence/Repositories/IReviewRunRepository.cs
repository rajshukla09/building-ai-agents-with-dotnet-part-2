using SoftwareReleaseReview.Api.Persistence.Entities;

namespace SoftwareReleaseReview.Api.Persistence.Repositories;

public interface IReviewRunRepository
{
    Task AddAsync(ReviewRunEntity run, CancellationToken cancellationToken);
    Task<ReviewRunEntity?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReviewRunEntity>> ListAsync(CancellationToken cancellationToken);
}
