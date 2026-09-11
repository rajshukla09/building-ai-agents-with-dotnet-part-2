namespace SoftwareReleaseReview.Api.Contracts;

public sealed record ReviewRunDto(Guid Id, string Pattern, string ReleaseRequest, string Status,
    DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, long DurationMilliseconds,
    string? FinalDecision, bool Success, string? Error,
    IReadOnlyList<AgentExecutionDto> AgentExecutions, IReadOnlyList<PersistedReviewEventDto> Events);

public sealed record AgentExecutionDto(int Sequence, string AgentName, DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt, long DurationMilliseconds, string Status, string? Finding);

public sealed record PersistedReviewEventDto(int Sequence, string EventType, string? Agent,
    DateTimeOffset Timestamp, string? Summary);

public sealed record RunComparisonDto(IReadOnlyList<PatternMetricsDto> Runs);

public sealed record PatternMetricsDto(Guid Id, string Pattern, long DurationMilliseconds,
    int AgentExecutions, int UniqueAgents, int? Handoffs, int? ConversationTurns,
    int? ParallelBranches, string? FinalDecision, IReadOnlyList<string> ExecutionOrder,
    IReadOnlyList<string> HandoffPath, string? SlowestBranch, bool? MaximumTurnsReached,
    ConcurrentTimingDto? ConcurrentTiming, IReadOnlyDictionary<string, int>? Contributions);

public sealed record ConcurrentTimingDto(DateTimeOffset FanOutStarted, DateTimeOffset FanInStarted,
    long TotalDurationMilliseconds, bool ExecutionsOverlap, IReadOnlyList<BranchTimingDto> Branches);

public sealed record BranchTimingDto(string Agent, DateTimeOffset StartedAt, DateTimeOffset CompletedAt,
    long DurationMilliseconds);
