using EnterpriseArchitectureAssessment.Api.Contracts;
using EnterpriseArchitectureAssessment.Api.Persistence;
namespace EnterpriseArchitectureAssessment.Api.Orchestration;
public sealed class AssessmentRunState(AssessmentRun run)
{
    private readonly HashSet<string> _observedManagerLines = new(StringComparer.Ordinal);
    public AssessmentRun Run => run;
    public void ApplyNativeResult(NativeMagenticResult result)
    {
        foreach (var nativeEvent in result.Events)
        {
            if (IsSpecialist(nativeEvent.Executor) && nativeEvent.RuntimeType.Contains("Invoked", StringComparison.OrdinalIgnoreCase))
            {
                if (!run.AgentInvocations.Any(x => x.Agent == nativeEvent.Executor && x.CompletedAt is null))
                    run.AgentInvocations.Add(new(Guid.NewGuid(), nativeEvent.Executor,
                        nativeEvent.Text ?? string.Empty, DateTimeOffset.UtcNow));
                Add(AssessmentEventTypes.AgentStarted, nativeEvent.Text ?? "Native participant invoked.", nativeEvent.Executor);
            }

            if (IsSpecialist(nativeEvent.Executor) && nativeEvent.RuntimeType.Contains("Completed", StringComparison.OrdinalIgnoreCase))
            {
                var invocation = run.AgentInvocations.LastOrDefault(x => x.Agent == nativeEvent.Executor && x.CompletedAt is null);
                if (invocation is not null)
                {
                    run.AgentInvocations.Remove(invocation);
                    run.AgentInvocations.Add(invocation with { CompletedAt = DateTimeOffset.UtcNow, Result = nativeEvent.Text });
                }
                Add(AssessmentEventTypes.AgentCompleted, nativeEvent.Text ?? "Native participant completed.", nativeEvent.Executor);
            }

            // These labels are manager-authored observable output, not reconstructed chain-of-thought.
            if (nativeEvent.Text is not null) ApplyManagerOutput(nativeEvent.Text);
        }

        if (result.FinalAssistantMessage is not null && string.IsNullOrWhiteSpace(run.CompletionReason))
            run.CompletionReason = "Native workflow produced a final assistant message.";
    }
    private static bool IsSpecialist(string? executor) =>
        executor is not null && executor.EndsWith("Agent", StringComparison.Ordinal) &&
        !executor.Contains("Manager", StringComparison.OrdinalIgnoreCase);
    public void ApplyManagerOutput(string output)
    {
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!_observedManagerLines.Add(line)) continue;
            var split = line.IndexOf(':');
            if (split < 1)
                continue;
            var tag = line[..split].Trim().ToUpperInvariant();
            var body = line[(split + 1)..].Trim();
            switch (tag)
            {
            case "PLAN":
                SetPlan(body, "Initial manager plan");
                break;
            case "REVISION":
                Revise(body);
                break;
            case "DELEGATION":
                Delegate(body);
                break;
            case "PROGRESS":
                run.ProgressEvaluations.Add(body);
                Add(AssessmentEventTypes.ProgressEvaluated, body);
                break;
            case "CONFLICT":
                run.Conflicts.Add(new(body, [body], string.Empty));
                Add(AssessmentEventTypes.ConflictDetected, body);
                break;
            case "QUESTION":
                run.OpenQuestions.Add(body);
                break;
            case "COMPLETE":
                run.CompletionReason = body;
                Add(AssessmentEventTypes.CompletionDeclared, body);
                break;
            }
        }
    }
    private void SetPlan(string body, string rationale)
    {
        var plan =
            new AssessmentPlan(run.CurrentPlan is null ? 1 : run.CurrentPlan.Version + 1,
                               body.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                   .Select(x => new AssessmentPlanItem(x))
                                   .ToArray(),
                               rationale, DateTimeOffset.UtcNow);
        run.InitialPlan ??= plan;
        run.CurrentPlan = plan;
        Add(run.InitialPlan == plan ? AssessmentEventTypes.PlanCreated : AssessmentEventTypes.PlanRevised, body);
    }
    private void Revise(string body)
    {
        var old = run.CurrentPlan?.Version ?? 0;
        SetPlan(body, "Replanned from new evidence");
        run.PlanRevisions.Add(new(old, run.CurrentPlan!.Version,
                                  run.ProgressEvaluations.LastOrDefault() ?? string.Empty, body,
                                  DateTimeOffset.UtcNow));
    }
    private void Delegate(string body)
    {
        var separator = body.IndexOfAny(['—', '-']);
        var agent = separator > 0 ? body[..separator].Trim() : body;
        var task = separator > 0 ? body[(separator + 1)..].Trim() : body;
        run.AgentInvocations.Add(new(Guid.NewGuid(), agent, task, DateTimeOffset.UtcNow));
        Add(AssessmentEventTypes.TaskDelegated, task, agent);
    }
    public void Add(string type, string summary, string? agent = null,
                    string? tool = null) => run.Events.Add(new(run.Events.Count + 1, type, DateTimeOffset.UtcNow,
                                                               summary, agent, tool));
}
