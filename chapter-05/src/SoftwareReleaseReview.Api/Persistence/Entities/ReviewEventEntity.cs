namespace SoftwareReleaseReview.Api.Persistence.Entities;

public sealed class ReviewEventEntity
{
    public long Id { get; set; }
    public Guid ReviewRunId { get; set; }
    public ReviewRunEntity ReviewRun { get; set; } = null!;
    public int Sequence { get; set; }
    public required string EventType { get; set; }
    public string? Agent { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string? Summary { get; set; }
}
