using Microsoft.EntityFrameworkCore;
using SoftwareReleaseReview.Api.Persistence.Entities;

namespace SoftwareReleaseReview.Api.Persistence.Repositories;

public sealed class ReviewRunRepository(ReviewDbContext db) : IReviewRunRepository
{
    public async Task AddAsync(ReviewRunEntity run, CancellationToken cancellationToken)
    {
        db.ReviewRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<ReviewRunEntity?> GetAsync(Guid id, CancellationToken cancellationToken) => db.ReviewRuns
        .AsNoTracking().Include(x => x.AgentExecutions).Include(x => x.Events)
        .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ReviewRunEntity>> ListAsync(CancellationToken cancellationToken)
    {
        var runs = await db.ReviewRuns
        .AsNoTracking().Include(x => x.AgentExecutions).Include(x => x.Events)
        .ToListAsync(cancellationToken);
        return runs.OrderByDescending(x => x.StartedAt).ToArray();
    }
}
