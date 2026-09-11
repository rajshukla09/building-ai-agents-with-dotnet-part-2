namespace ObservableReleaseReview.Api.Contracts;

public sealed record ValidatedRelease(
    string RunId,
    string Release,
    bool SimulateFailure,
    bool HighRisk);
