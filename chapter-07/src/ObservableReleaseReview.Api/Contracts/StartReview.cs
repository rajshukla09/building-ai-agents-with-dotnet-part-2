namespace ObservableReleaseReview.Api.Contracts;

internal sealed record StartReview(
    string RunId,
    string Release,
    bool SimulateFailure);
