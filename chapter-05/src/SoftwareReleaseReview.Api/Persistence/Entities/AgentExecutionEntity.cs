namespace SoftwareReleaseReview.Api.Persistence.Entities;

public sealed class AgentExecutionEntity
{
    public long Id { get; set; }
    public Guid ReviewRunId { get; set; }
    public ReviewRunEntity ReviewRun { get; set; } = null!;
    public int Sequence { get; set; }
    public required string AgentName { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
    public long DurationMilliseconds { get; set; }
    public required string Status { get; set; }
    public string? Finding { get; set; }
}
