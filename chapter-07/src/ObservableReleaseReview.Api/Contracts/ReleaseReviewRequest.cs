using System.ComponentModel.DataAnnotations;

namespace ObservableReleaseReview.Api.Contracts;

public sealed record ReleaseReviewRequest(
    [Required] string Release,
    bool SimulateFailure = false);
