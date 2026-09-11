namespace SoftwareReleaseReview.Api.Persistence.Entities;

public sealed class ReviewRunEntity
{
    public Guid Id { get; set; }
    public required string Pattern { get; set; }
    public required string ReleaseRequest { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public long DurationMilliseconds { get; set; }
    public string? FinalDecision { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<AgentExecutionEntity> AgentExecutions { get; set; } = [];
    public List<ReviewEventEntity> Events { get; set; } = [];
}
