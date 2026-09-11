namespace SoftwareReleaseReview.Web.Models;

public sealed record ReviewRequest(string Release);

public sealed record ReviewResponse(string Pattern, string Decision, IReadOnlyList<ReviewEvent> Events);

public sealed record ReviewEvent(string RuntimeType, string? Executor, string? Text, DateTimeOffset Timestamp);

public sealed record ReviewRun(Guid Id, string Pattern, string ReleaseRequest, string Status,
    DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, long DurationMilliseconds,
    string? FinalDecision, bool Success, string? Error,
    IReadOnlyList<AgentExecution> AgentExecutions, IReadOnlyList<PersistedReviewEvent> Events);

public sealed record AgentExecution(int Sequence, string AgentName, DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt, long DurationMilliseconds, string Status, string? Finding);

public sealed record PersistedReviewEvent(int Sequence, string EventType, string? Agent,
    DateTimeOffset Timestamp, string? Summary);

public sealed record RunComparison(IReadOnlyList<PatternMetrics> Runs);

public sealed record PatternMetrics(Guid Id, string Pattern, long DurationMilliseconds,
    int AgentExecutions, int UniqueAgents, int? Handoffs, int? ConversationTurns,
    int? ParallelBranches, string? FinalDecision, IReadOnlyList<string> ExecutionOrder,
    IReadOnlyList<string> HandoffPath, string? SlowestBranch, bool? MaximumTurnsReached,
    ConcurrentTiming? ConcurrentTiming, IReadOnlyDictionary<string, int>? Contributions);

public sealed record ConcurrentTiming(DateTimeOffset FanOutStarted, DateTimeOffset FanInStarted,
    long TotalDurationMilliseconds, bool ExecutionsOverlap, IReadOnlyList<BranchTiming> Branches);

public sealed record BranchTiming(string Agent, DateTimeOffset StartedAt, DateTimeOffset CompletedAt,
    long DurationMilliseconds);
