namespace ObservableReleaseReview.Api.Contracts;

public sealed record ExecutorExecution(
    string Name,
    string Status,
    long? DurationMs = null);
