using Microsoft.AspNetCore.Mvc;
using SoftwareReleaseReview.Api.Contracts;
using SoftwareReleaseReview.Api.Orchestrations;
using SoftwareReleaseReview.Api.Persistence.Repositories;
using SoftwareReleaseReview.Api.Services;

namespace SoftwareReleaseReview.Api.Controllers;

[ApiController]
[Route("api/reviews")]
public sealed class ReviewsController(ReviewExecutionService execution, IReviewRunRepository runs) : ControllerBase
{
    [HttpPost("sequential")]
    public Task<ReviewResponse> Sequential(ReviewRequest request, CancellationToken ct) =>
        execution.RunAsync("Sequential", request.Release, ct);

    [HttpPost("concurrent")]
    public Task<ReviewResponse> Concurrent(ReviewRequest request, CancellationToken ct) =>
        execution.RunAsync("Concurrent", request.Release, ct);

    [HttpPost("handoff")]
    public Task<ReviewResponse> Handoff(ReviewRequest request, CancellationToken ct) =>
        execution.RunAsync("Handoff", request.Release, ct);

    [HttpPost("group-chat")]
    public Task<ReviewResponse> GroupChat(ReviewRequest request, CancellationToken ct) =>
        execution.RunAsync("GroupChat", request.Release, ct);

    [HttpGet("runs")]
    public async Task<IReadOnlyList<ReviewRunDto>> Runs(CancellationToken ct) =>
        (await runs.ListAsync(ct)).Select(ReviewRunMapper.ToDto).ToArray();

    [HttpGet("runs/{id:guid}")]
    public async Task<ActionResult<ReviewRunDto>> Run(Guid id, CancellationToken ct)
    {
        var run = await runs.GetAsync(id, ct);
        if (run is null) return NotFound();
        return ReviewRunMapper.ToDto(run);
    }

    [HttpGet("compare")]
    public async Task<ActionResult<RunComparisonDto>> Compare([FromQuery] string ids, CancellationToken ct)
    {
        var values = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length is < 2 or > 4 || values.Any(value => !Guid.TryParse(value, out _)))
            return BadRequest("Select between two and four valid run IDs.");
        var parsed = values.Select(Guid.Parse).Distinct().ToArray();
        if (parsed.Length < 2) return BadRequest("Select at least two different runs.");
        var selected = await Task.WhenAll(parsed.Select(id => runs.GetAsync(id, ct)));
        if (selected.Any(x => x is null)) return NotFound();
        return RunComparisonService.Compare(selected.Select(x => x!));
    }
}
