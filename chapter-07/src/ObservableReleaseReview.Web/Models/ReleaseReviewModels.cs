namespace ObservableReleaseReview.Web.Models;

public sealed record ReleaseReviewRequest(string Release, bool SimulateFailure);

public sealed record ReleaseReviewResponse(
    string RunId,
    string Status,
    string? FinalDecision,
    long DurationMs,
    bool HighRisk,
    string RoutingOutcome,
    IReadOnlyList<ExecutorExecution> Executors,
    string? Error);

public sealed record ExecutorExecution(
    string Name,
    string Status,
    long? DurationMs);
