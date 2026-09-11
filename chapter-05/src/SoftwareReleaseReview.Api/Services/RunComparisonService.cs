using SoftwareReleaseReview.Api.Contracts;
using SoftwareReleaseReview.Api.Persistence.Entities;

namespace SoftwareReleaseReview.Api.Services;

public static class RunComparisonService
{
    public static RunComparisonDto Compare(IEnumerable<ReviewRunEntity> runs) => new(runs.Select(ToMetrics).ToArray());

    public static PatternMetricsDto ToMetrics(ReviewRunEntity run)
    {
        var agents = run.AgentExecutions
            .Select(x => (Execution: x, Name: BusinessAgentNames.Normalize(x.AgentName)))
            .Where(x => x.Name is not null)
            .GroupBy(x => x.Name!, StringComparer.Ordinal)
            .Select(group => new AgentWindow(group.Key, group.Min(x => x.Execution.Sequence),
                group.Min(x => x.Execution.StartedAt), group.Max(x => x.Execution.CompletedAt)))
            .OrderBy(x => x.Sequence).ToArray();
        var handoffEvents = run.Events.Where(x => x.EventType.Contains("Handoff", StringComparison.OrdinalIgnoreCase)).ToArray();
        var turnEvents = run.Events.Where(x => x.EventType.Contains("TurnCompleted", StringComparison.OrdinalIgnoreCase)).ToArray();
        var path = run.Pattern == "Handoff" ? agents.Select(x => x.Name).ToArray() : [];
        var branches = run.Pattern == "Concurrent"
            ? agents.Where(x => x.Name is "SecurityAgent" or "QualityAgent" or "ArchitectureAgent").ToArray()
            : [];
        var slowest = branches.MaxBy(x => x.DurationMilliseconds)?.Name;
        var concurrentTiming = branches.Length > 0
            ? new ConcurrentTimingDto(branches.Min(x => x.StartedAt), branches.Max(x => x.CompletedAt),
                (long)(branches.Max(x => x.CompletedAt) - branches.Min(x => x.StartedAt)).TotalMilliseconds,
                branches.SelectMany((left, i) => branches.Skip(i + 1).Select(right => (left, right)))
                    .Any(pair => pair.left.StartedAt < pair.right.CompletedAt && pair.right.StartedAt < pair.left.CompletedAt),
                branches.Select(x => new BranchTimingDto(x.Name, x.StartedAt, x.CompletedAt, x.DurationMilliseconds)).ToArray())
            : null;
        var contributions = run.Pattern == "GroupChat"
            ? run.Events.Select(x => (Event: x, Agent: BusinessAgentNames.Normalize(x.Agent)))
                .Where(x => x.Agent is not null).GroupBy(x => x.Agent!)
                .ToDictionary(x => x.Key, x => x.Count())
            : null;
        return new PatternMetricsDto(run.Id, run.Pattern, run.DurationMilliseconds, agents.Length,
            agents.Length, run.Pattern == "Handoff" ? handoffEvents.Length : null,
            run.Pattern == "GroupChat" ? turnEvents.Length : null, run.Pattern == "Concurrent" ? branches.Length : null,
            FinalDecision(run), ExecutionPath(run.Pattern, agents.Select(x => x.Name)), path, slowest,
            run.Pattern == "GroupChat" ? turnEvents.Length >= 6 : null, concurrentTiming, contributions);
    }

    private static IReadOnlyList<string> ExecutionPath(string pattern, IEnumerable<string> observed) => pattern switch
    {
        "Sequential" => BusinessAgentNames.OrderedFor("Sequential"),
        "Concurrent" => ["Fan-Out", "SecurityAgent | QualityAgent | ArchitectureAgent", "Fan-In", "ReleaseAgent"],
        _ => observed.ToArray()
    };

    private static string? FinalDecision(ReviewRunEntity run)
    {
        var release = run.AgentExecutions
            .Where(x => BusinessAgentNames.Normalize(x.AgentName) == "ReleaseAgent")
            .OrderBy(x => x.Sequence).Select(x => x.Finding)
            .LastOrDefault(x => !string.IsNullOrWhiteSpace(x));
        return release ?? run.FinalDecision;
    }

    private sealed record AgentWindow(string Name, int Sequence, DateTimeOffset StartedAt, DateTimeOffset CompletedAt)
    {
        public long DurationMilliseconds => Math.Max(0, (long)(CompletedAt - StartedAt).TotalMilliseconds);
    }
}
