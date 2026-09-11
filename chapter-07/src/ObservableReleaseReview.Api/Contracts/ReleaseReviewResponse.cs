namespace ObservableReleaseReview.Api.Contracts;

public sealed record ReleaseReviewResponse(
    string RunId,
    string Status,
    string? FinalDecision,
    long DurationMs,
    bool HighRisk,
    string RoutingOutcome,
    IReadOnlyList<ExecutorExecution> Executors,
    string? Error);
