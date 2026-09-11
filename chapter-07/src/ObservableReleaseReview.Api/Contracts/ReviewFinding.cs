namespace ObservableReleaseReview.Api.Contracts;

public sealed record ReviewFinding(
    string RunId,
    string Area,
    string Finding,
    bool HighRisk);
