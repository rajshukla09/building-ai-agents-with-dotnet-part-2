using EnterpriseArchitectureAssessment.Api.Contracts;
using EnterpriseArchitectureAssessment.Api.Persistence;
namespace EnterpriseArchitectureAssessment.Api.Orchestration;
public sealed class AssessmentEventWriter
{
    private readonly AsyncLocal<AssessmentRun?> _current = new();
    public IDisposable Begin(AssessmentRun run)
    {
        _current.Value = run;
        return new Scope(() => _current.Value = null);
    }
    public void ToolCalled(Guid invocationId, string server, string tool, string arguments, bool cached,
                           long durationMilliseconds, bool success, string? error = null)
    {
        if (_current.Value is not {} run)
            return;
        run.ToolInvocations.Add(new(invocationId, server, tool, arguments, cached, DateTimeOffset.UtcNow,
                                    durationMilliseconds, success, error));
        run.Events.Add(new(run.Events.Count + 1, AssessmentEventTypes.McpToolCalled, DateTimeOffset.UtcNow,
                           !success ? "MCP tool failed." : cached ? "Duplicate call served from invocation cache." : "Read-only MCP tool executed.",
                           server, tool));
    }
    private sealed class Scope(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
