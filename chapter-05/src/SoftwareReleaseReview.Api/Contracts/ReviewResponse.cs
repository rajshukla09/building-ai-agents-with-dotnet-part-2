using System.Text.Json.Serialization;

namespace SoftwareReleaseReview.Api.Contracts;

public sealed record ReviewResponse(string Pattern, string Decision, IReadOnlyList<ReviewEvent> Events,
    [property: JsonIgnore] IReadOnlyList<ReviewEvent> RawEvents);

public sealed record ReviewEvent(string RuntimeType, string? Executor, string? Text, DateTimeOffset Timestamp);
