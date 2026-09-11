using System.Text;
using SoftwareReleaseReview.Api.Contracts;
using SoftwareReleaseReview.Api.Services;

namespace SoftwareReleaseReview.Api.Orchestrations;

internal static class ReadableEventProjector
{
    public static IReadOnlyList<ReviewEvent> Project(string pattern, IReadOnlyList<ReviewEvent> raw)
    {
        var firstTimestamp = raw.FirstOrDefault()?.Timestamp ?? DateTimeOffset.UtcNow;
        var result = new List<ReviewEvent>
        {
            new("WorkflowStarted", null, $"{pattern} workflow started.", firstTimestamp)
        };

        var groups = raw.Select((item, index) => (item, index, agent: BusinessAgentNames.Normalize(item.Executor)))
            .Where(x => x.agent is not null)
            .GroupBy(x => x.agent!, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var preferredOrder = BusinessAgentNames.OrderedFor(pattern);
        var orderedNames = preferredOrder.Count > 0
            ? preferredOrder.Where(groups.ContainsKey)
            : groups.OrderBy(x => x.Value.Min(item => item.index)).Select(x => x.Key);

        foreach (var name in orderedNames)
        {
            var agentEvents = groups[name];
            var contribution = Contribution(agentEvents.Select(x => x.item).ToArray());
            result.Add(new ReviewEvent("AgentStarted", name, "Started.",
                agentEvents.Min(x => x.item.Timestamp)));
            result.Add(new ReviewEvent("AgentCompleted", name, contribution,
                agentEvents.Max(x => x.item.Timestamp)));
        }

        // Handoff envelopes are useful orchestration facts, but token/update infrastructure is not.
        result.AddRange(raw.Where(x => x.RuntimeType.Contains("Handoff", StringComparison.OrdinalIgnoreCase))
            .Select(x => x with
            {
                RuntimeType = "Handoff",
                Executor = BusinessAgentNames.Normalize(x.Executor) ?? x.Executor
            }));
        result.Add(new ReviewEvent("WorkflowCompleted", null, $"{pattern} workflow completed.",
            raw.LastOrDefault()?.Timestamp ?? firstTimestamp));
        return result;
    }

    private static string Contribution(IReadOnlyList<ReviewEvent> events)
    {
        var completedResponse = Array.Empty<string?>();
        var currentResponse = new List<string?>();
        foreach (var item in events)
        {
            if (item.RuntimeType.Contains("ResponseUpdate", StringComparison.OrdinalIgnoreCase) && IsText(item.Text))
                currentResponse.Add(item.Text);
            if (item.RuntimeType.Contains("ExecutorCompleted", StringComparison.OrdinalIgnoreCase) && currentResponse.Count > 0)
            {
                completedResponse = currentResponse.ToArray();
                currentResponse.Clear();
            }
        }
        var chunks = currentResponse.Count > 0 ? currentResponse.ToArray() : completedResponse;
        if (chunks.Length > 0) return JoinChunks(chunks!);

        return events.Where(x => !IsInfrastructure(x.RuntimeType)).Select(x => x.Text)
            .LastOrDefault(IsText) ?? "Completed without a textual contribution.";
    }

    private static string JoinChunks(IEnumerable<string?> chunks)
    {
        var text = new StringBuilder();
        foreach (var chunk in chunks.Select(x => x!)) text.Append(chunk);
        return text.ToString().Trim();
    }

    private static bool IsText(string? value) => !string.IsNullOrWhiteSpace(value) && value != ".";

    private static bool IsInfrastructure(string type) =>
        type.Contains("ExecutorCompleted", StringComparison.OrdinalIgnoreCase) ||
        type.Contains("OutputMessages", StringComparison.OrdinalIgnoreCase);
}
