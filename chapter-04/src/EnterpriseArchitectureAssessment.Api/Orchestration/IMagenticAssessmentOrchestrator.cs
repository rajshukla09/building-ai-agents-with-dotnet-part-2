namespace EnterpriseArchitectureAssessment.Api.Orchestration;

public interface IMagenticAssessmentOrchestrator
{
    Task<NativeMagenticResult> RunAsync(string objective, CancellationToken cancellationToken);
}

public sealed record NativeMagenticResult(
    string? FinalAssistantMessage,
    IReadOnlyList<NativeMagenticEvent> Events);

public sealed record NativeMagenticEvent(string RuntimeType, string? Executor, string? Text);
