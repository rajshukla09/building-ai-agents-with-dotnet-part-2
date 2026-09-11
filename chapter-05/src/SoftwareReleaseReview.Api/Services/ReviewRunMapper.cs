using SoftwareReleaseReview.Api.Contracts;
using SoftwareReleaseReview.Api.Persistence.Entities;

namespace SoftwareReleaseReview.Api.Services;

public static class ReviewRunMapper
{
    public static ReviewRunDto ToDto(ReviewRunEntity run) => new(run.Id, run.Pattern, run.ReleaseRequest,
        run.Status, run.StartedAt, run.CompletedAt, run.DurationMilliseconds, run.FinalDecision, run.Success, run.Error,
        run.AgentExecutions.Select(x => (Execution: x, Name: BusinessAgentNames.Normalize(x.AgentName)))
            .Where(x => x.Name is not null).OrderBy(x => x.Execution.Sequence)
            .Select((x, index) => new AgentExecutionDto(index + 1, x.Name!,
                x.Execution.StartedAt, x.Execution.CompletedAt, x.Execution.DurationMilliseconds,
                x.Execution.Status, x.Execution.Finding)).ToArray(),
        run.Events.OrderBy(x => x.Sequence).Select(x => new PersistedReviewEventDto(x.Sequence, x.EventType,
            x.Agent, x.Timestamp, x.Summary)).ToArray());
}
