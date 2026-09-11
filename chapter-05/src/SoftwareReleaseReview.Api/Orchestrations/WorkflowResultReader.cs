using Microsoft.Agents.AI.Workflows;
using SoftwareReleaseReview.Api.Contracts;
using SoftwareReleaseReview.Api.Services;

namespace SoftwareReleaseReview.Api.Orchestrations;

internal static class WorkflowResultReader
{
    public static ReviewResponse Read(string pattern, IEnumerable<WorkflowEvent> events)
    {
        var raw = events.Select(@event => new ReviewEvent(
            @event.GetType().Name,
            ReadProperty(@event, "ExecutorId", "ExecutorName", "AgentName"),
            ReadText(@event.GetType().GetProperty("Data")?.GetValue(@event)),
            ReadTimestamp(@event))).ToArray();
        var readable = ReadableEventProjector.Project(pattern, raw);
        var decision = readable
            .Where(x => BusinessAgentNames.Normalize(x.Executor) == "ReleaseAgent")
            .Select(x => x.Text)
            .LastOrDefault(x => !string.IsNullOrWhiteSpace(x))
            ?? (pattern.Contains("Concurrent reviews", StringComparison.Ordinal)
                ? string.Join("\n\n", readable.Where(x => BusinessAgentNames.IsReviewAgent(x.Executor)).Select(x => $"{x.Executor}: {x.Text}"))
                : "Workflow completed without a ReleaseAgent decision.");
        return new ReviewResponse(pattern, decision, readable, raw);
    }

    private static string? ReadProperty(object value, params string[] names) => names
        .Select(name => value.GetType().GetProperty(name)?.GetValue(value)?.ToString())
        .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

    private static DateTimeOffset ReadTimestamp(object value)
    {
        foreach (var name in new[] { "Timestamp", "CreatedAt", "Time" })
        {
            var raw = value.GetType().GetProperty(name)?.GetValue(value);
            if (raw is DateTimeOffset offset) return offset;
            if (raw is DateTime dateTime) return new DateTimeOffset(dateTime);
        }

        // Receipt time is used only when this package version does not publish an event timestamp.
        return DateTimeOffset.UtcNow;
    }

    private static string? ReadText(object? value)
    {
        var values = new List<string>();
        Collect(value, values, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
        return values.Count == 0 ? null : string.Join("\n", values.Distinct());
    }

    private static void Collect(object? value, List<string> values, HashSet<object> seen, int depth)
    {
        if (value is null || depth > 7) return;
        if (value is string text)
        {
            // Preserve leading/trailing spaces on streaming chunks so "release" + " decision"
            // does not become "releasedecision" when the readable projection joins them.
            if (!string.IsNullOrWhiteSpace(text) && text.Trim() != ".") values.Add(text);
            return;
        }
        if (!value.GetType().IsValueType && !seen.Add(value)) return;
        if (value is System.Collections.IEnumerable sequence)
        {
            foreach (var item in sequence) Collect(item, values, seen, depth + 1);
            return;
        }
        foreach (var name in new[] { "Text", "Content", "Contents", "Message", "Messages", "Result", "Value" })
        {
            var property = value.GetType().GetProperty(name);
            if (property is not null && property.GetIndexParameters().Length == 0)
                Collect(property.GetValue(value), values, seen, depth + 1);
        }
    }
}
