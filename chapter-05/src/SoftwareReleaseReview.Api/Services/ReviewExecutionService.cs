using System.Diagnostics;
using SoftwareReleaseReview.Api.Contracts;
using SoftwareReleaseReview.Api.Orchestrations;
using SoftwareReleaseReview.Api.Persistence.Entities;
using SoftwareReleaseReview.Api.Persistence.Repositories;

namespace SoftwareReleaseReview.Api.Services;

public sealed class ReviewExecutionService(IReviewRunRepository repository,
    SequentialReview sequential, ConcurrentReview concurrent, HandoffReview handoff, GroupChatReview groupChat)
{
    public async Task<ReviewResponse> RunAsync(string pattern, string release, CancellationToken cancellationToken)
    {
        var normalized = NormalizePattern(pattern);
        var startedAt = DateTimeOffset.UtcNow;
        var timer = Stopwatch.StartNew();
        try
        {
            var result = await Resolve(normalized).RunAsync(release, cancellationToken);
            timer.Stop();
            await repository.AddAsync(BuildRun(normalized, release, result, startedAt, timer.ElapsedMilliseconds), cancellationToken);
            return result;
        }
        catch (Exception exception)
        {
            timer.Stop();
            await repository.AddAsync(new ReviewRunEntity
            {
                Id = Guid.NewGuid(), Pattern = normalized, ReleaseRequest = release, Status = "Failed",
                StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow,
                DurationMilliseconds = timer.ElapsedMilliseconds, Success = false, Error = exception.Message
            }, CancellationToken.None);
            throw;
        }
    }

    private IReleaseReview Resolve(string pattern) => pattern switch
    {
        "Sequential" => sequential, "Concurrent" => concurrent, "Handoff" => handoff,
        "GroupChat" => groupChat, _ => throw new ArgumentOutOfRangeException(nameof(pattern))
    };

    private static string NormalizePattern(string pattern) => pattern.ToLowerInvariant() switch
    {
        "sequential" => "Sequential", "concurrent" => "Concurrent", "handoff" => "Handoff",
        "group-chat" or "groupchat" => "GroupChat", _ => throw new ArgumentException("Unknown review pattern.")
    };

    private static ReviewRunEntity BuildRun(string pattern, string release, ReviewResponse result,
        DateTimeOffset startedAt, long duration)
    {
        var run = new ReviewRunEntity
        {
            Id = Guid.NewGuid(), Pattern = pattern, ReleaseRequest = release, Status = "Completed",
            StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow, DurationMilliseconds = duration,
            FinalDecision = result.Decision, Success = true
        };
        // Raw envelopes remain durable for later observability work; the normal UI receives Events.
        run.Events = result.RawEvents.Select((x, index) => new ReviewEventEntity
        {
            Sequence = index + 1, EventType = x.RuntimeType, Agent = x.Executor,
            Timestamp = x.Timestamp, Summary = x.Text
        }).ToList();

        // Executions are reconstructed only from observable native event envelopes bearing an executor.
        run.AgentExecutions = result.RawEvents
            .Select(x => (Event: x, Agent: BusinessAgentNames.Normalize(x.Executor)))
            .Where(x => x.Agent is not null)
            .GroupBy(x => x.Agent!, StringComparer.Ordinal)
            .Select((events, index) =>
            {
                var ordered = events.Select(x => x.Event).OrderBy(x => x.Timestamp).ToArray();
                var first = ordered[0].Timestamp;
                var last = ordered[^1].Timestamp;
                return new AgentExecutionEntity
                {
                    Sequence = index + 1, AgentName = events.Key, StartedAt = first, CompletedAt = last,
                    DurationMilliseconds = Math.Max(0, (long)(last - first).TotalMilliseconds), Status = "Completed",
                    Finding = result.Events
                        .Where(x => x.RuntimeType == "AgentCompleted" &&
                                    BusinessAgentNames.Normalize(x.Executor) == events.Key)
                        .Select(x => x.Text).LastOrDefault(x => !string.IsNullOrWhiteSpace(x))
                };
            }).OrderBy(x => x.StartedAt).ThenBy(x => x.AgentName).Select((x, index) => { x.Sequence = index + 1; return x; }).ToList();
        return run;
    }
}
