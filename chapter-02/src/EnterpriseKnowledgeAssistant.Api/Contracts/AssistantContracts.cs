namespace EnterpriseKnowledgeAssistant.Api.Contracts;

public sealed record AssistantQueryRequest(string Query);
public sealed record ToolInvocationResponse(
    int Sequence,
    string Server,
    string Tool,
    long DurationMilliseconds,
    bool Succeeded,
    string? Arguments = null,
    string? Result = null,
    string? Error = null);
public sealed record AssistantQueryResponse(string Answer, IReadOnlyList<ToolInvocationResponse> Invocations, bool ToolCallLimitReached = false);
