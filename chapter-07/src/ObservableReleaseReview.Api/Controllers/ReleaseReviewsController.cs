using Microsoft.AspNetCore.Mvc;
using ObservableReleaseReview.Api.Contracts;
using ObservableReleaseReview.Api.Workflows;

namespace ObservableReleaseReview.Api.Controllers;

[ApiController]
[Route("api/releases")]
public sealed class ReleaseReviewsController(IReleaseReviewWorkflow workflow) : ControllerBase
{
    [HttpPost("review")]
    [ProducesResponseType<ReleaseReviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ReleaseReviewResponse>> Review(
        [FromBody] ReleaseReviewRequest request,
        CancellationToken cancellationToken)
    {
        var response = await workflow.ReviewAsync(request, cancellationToken);
        return Ok(response);
    }
}
