using EnterpriseArchitectureAssessment.Api.Contracts;
using EnterpriseArchitectureAssessment.Api.Persistence;
namespace EnterpriseArchitectureAssessment.Api.Orchestration;
public sealed class AssessmentService(IAssessmentRunStore store, IMagenticAssessmentOrchestrator orchestrator,
                                      AssessmentEventWriter events, ILogger<AssessmentService> logger)
    : IAssessmentService
{
    public async Task<AssessmentRunResponse> StartAsync(StartAssessmentRequest request,
                                                        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Objective))
            throw new ArgumentException("An objective is required.");
        var run = store.Create(request.AssessmentId ?? Guid.NewGuid(), request.Objective);
        logger.LogInformation("Assessment run {AssessmentId} created for objective {Objective}", run.Id,
                              request.Objective);
        var state = new AssessmentRunState(run);
        state.Add(AssessmentEventTypes.AssessmentStarted, "Objective submitted to native MAF Magentic orchestration.");
        run.Status = "Running";
        store.Save(run);
        try
        {
            using var executionScope = events.Begin(run);
            var result = await orchestrator.RunAsync(request.Objective, cancellationToken);
            state.ApplyNativeResult(result);
            run.FinalRecommendation = FinalRecommendation(result.FinalAssistantMessage);
            run.Status = "Completed";
            run.CompletedAt = DateTimeOffset.UtcNow;
            run.DurationMilliseconds = (long)(run.CompletedAt.Value - run.StartedAt).TotalMilliseconds;
            state.Add(AssessmentEventTypes.AssessmentCompleted, run.FinalRecommendation is null
                ? "Native Magentic workflow completed without a public textual output."
                : "Native Magentic workflow completed with an assistant recommendation.");
            store.Save(run);
            logger.LogInformation("Assessment run {AssessmentId} completed with {EventCount} events", run.Id,
                                  run.Events.Count);
            return run;
        }
        catch (Exception ex)
        {
            run.Status = "Failed";
            run.CompletedAt = DateTimeOffset.UtcNow;
            run.DurationMilliseconds = (long)(run.CompletedAt.Value - run.StartedAt).TotalMilliseconds;
            state.Add(AssessmentEventTypes.AssessmentFailed, ex.Message);
            store.Save(run);
            logger.LogError(ex, "Assessment run {AssessmentId} failed", run.Id);
            throw;
        }
    }

    private static string? FinalRecommendation(string? assistantMessage)
    {
        if (!NativeEventText.IsMeaningful(assistantMessage)) return null;
        var completion = assistantMessage!.Split('\n', StringSplitOptions.TrimEntries)
            .LastOrDefault(x => x.StartsWith("COMPLETE:", StringComparison.OrdinalIgnoreCase));
        return completion is null ? assistantMessage.Trim() : completion[(completion.IndexOf(':') + 1)..].Trim();
    }
}
