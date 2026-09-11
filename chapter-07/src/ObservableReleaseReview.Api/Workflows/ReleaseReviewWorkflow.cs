using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Observability;
using Microsoft.Extensions.Options;
using ObservableReleaseReview.Api.Contracts;
using ObservableReleaseReview.Api.Executors;

namespace ObservableReleaseReview.Api.Workflows;

public sealed class ReleaseReviewWorkflow : IReleaseReviewWorkflow
{
    private readonly Workflow _workflow;

    public ReleaseReviewWorkflow(IOptions<WorkflowTelemetryOptions> telemetry)
    {
        SensitiveDataEnabled = telemetry.Value.EnableSensitiveData;
        _workflow = BuildWorkflow();
    }

    public bool SensitiveDataEnabled { get; }

    public async Task<ReleaseReviewResponse> ReviewAsync(
        ReleaseReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var runId = Activity.Current?.TraceId.ToString();
        if (string.IsNullOrEmpty(runId))
        {
            runId = ActivityTraceId.CreateRandom().ToString();
        }

        var stopwatch = Stopwatch.StartNew();
        var highRisk = IsHighRisk(request.Release);

        try
        {
            await using var run = await InProcessExecution.Concurrent.RunAsync(
                _workflow,
                new StartReview(runId, request.Release, request.SimulateFailure),
                runId,
                cancellationToken);

            var output = run.OutgoingEvents
                .OfType<WorkflowOutputEvent>()
                .Select(workflowEvent => workflowEvent.Data)
                .OfType<ReleaseDecisionResult>()
                .LastOrDefault(result => !result.Decision.StartsWith("Aggregating"));

            _ = await run.GetStatusAsync(cancellationToken);
            var status = output is null ? "Failed" : "Completed";

            return new ReleaseReviewResponse(
                runId,
                status,
                output?.Decision,
                stopwatch.ElapsedMilliseconds,
                highRisk,
                highRisk ? "HighRisk: yes" : "HighRisk: no",
                BuildExecutions(highRisk, failed: output is null),
                output is null && request.SimulateFailure
                    ? "Simulated security review failure."
                    : null);
        }
        catch
        {
            return new ReleaseReviewResponse(
                runId,
                "Failed",
                null,
                stopwatch.ElapsedMilliseconds,
                highRisk,
                highRisk ? "HighRisk: yes" : "HighRisk: no",
                BuildExecutions(highRisk, failed: true),
                request.SimulateFailure
                    ? "Simulated security review failure."
                    : "Workflow execution failed.");
        }
    }

    private static bool IsHighRisk(string release) =>
        release.Contains("authentication", StringComparison.OrdinalIgnoreCase)
        || release.Contains("payment", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<ExecutorExecution> BuildExecutions(
        bool highRisk,
        bool failed)
    {
        return
        [
            new("ValidateRelease", "Completed"),
            new("ExtraReview", highRisk ? "Completed" : "Skipped"),
            new("SecurityReview", failed ? "Failed" : "Completed"),
            new("QualityReview", "Completed"),
            new("ArchitectureReview", "Completed"),
            new("ReleaseDecision", failed ? "Not Executed" : "Completed")
        ];
    }

    private Workflow BuildWorkflow()
    {
        var validate = new ValidateRelease();
        var extraReview = new ExtraReview();
        var continueReview = new ContinueReview();
        var securityReview = new SecurityReview();
        var qualityReview = new QualityReview();
        var architectureReview = new ArchitectureReview();
        var decision = new ReleaseDecision(
            new ConcurrentDictionary<string, ConcurrentBag<ReviewFinding>>());

        return new WorkflowBuilder(validate)
            .AddEdge<ValidatedRelease>(
                validate,
                extraReview,
                release => release!.HighRisk,
                "HighRisk: yes")
            .AddEdge<ValidatedRelease>(
                validate,
                continueReview,
                release => !release!.HighRisk,
                "HighRisk: no")
            .AddFanOutEdge(
                extraReview,
                [securityReview, qualityReview, architectureReview],
                "extra review complete")
            .AddFanOutEdge(
                continueReview,
                [securityReview, qualityReview, architectureReview],
                "standard reviews")
            .AddFanInBarrierEdge(
                [securityReview, qualityReview, architectureReview],
                decision,
                "all reviews complete")
            .WithOutputFrom(decision)
            .WithName("ObservableReleaseReview")
            .WithOpenTelemetry(options =>
                options.EnableSensitiveData = SensitiveDataEnabled)
            .Build();
    }
}
