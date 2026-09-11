using System.Collections.Concurrent;
using System.Text.Json;
using ContextEngineering.Api.Models;
using ContextEngineering.Api.Persistence;

namespace ContextEngineering.Api.Observability;

public sealed record SupervisorStateView(Guid IncidentId, string CurrentGoal, string Status, IReadOnlyList<string> CompletedInvestigations, IReadOnlyList<string> PendingInvestigations, IReadOnlyList<ActiveHypothesis> ActiveHypotheses, IReadOnlyList<EvidenceReference> EvidenceReferences, double Confidence, int ApproximateCharacters);

public sealed record EvidenceSummaryView(Guid EvidenceId, string SourceAgent, EvidenceType EvidenceType, string Component, DateTimeOffset? ObservedFrom, DateTimeOffset? ObservedTo, string Summary, int Importance, DateTimeOffset CreatedAt, int ApproximateCharacters, int InvestigationRound);

public sealed record InvestigationTimelineEvent(int Sequence, DateTimeOffset Timestamp, string EventType, string Message, string? Agent = null, int? RetrievalIteration = null);

public sealed record AgentExecutionView(Guid ExecutionId, string Agent, string Task, int RetrievalIteration, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, string Status, ContextBundle? ContextBundle, string? FindingSummary, Guid? NewEvidenceId, AdditionalContextRequirement? AdditionalContextRequired);

public sealed record FinalRecommendationView(string LikelyRootCause, string Recommendation, string Status, double Confidence, IReadOnlyList<Guid> SupportingEvidenceIds);

public sealed record InvestigationMetricsView(int StoredEvidenceRecords, int ApproximateStoredCharacters, int ApproximateSupervisorStateCharacters, int CurrentContextRecords, int ApproximateCurrentContextCharacters);

public sealed record InvestigationView(Guid IncidentId, string IncidentSummary, string Status, SupervisorStateView? SupervisorState, IReadOnlyList<InvestigationTimelineEvent> Timeline, IReadOnlyList<AgentExecutionView> AgentExecutions, IReadOnlyList<EvidenceSummaryView> Evidence, InvestigationMetricsView Metrics, FinalRecommendationView? FinalRecommendation, string? FailureMessage = null);

public interface IInvestigationObserver
{
    void Start(Guid incidentId, string incidentSummary);

    void AgentSelected(Guid incidentId, string agent, string task);

    void RetrievalStarted(Guid incidentId, string agent, string task, int iteration);

    void ContextAssembled(Guid incidentId, string agent, string task, ContextBundle bundle);

    void AgentStarted(Guid incidentId, string agent, string task, int iteration);

    void FindingStored(Guid incidentId, string agent, string task, SpecialistFinding finding, Guid evidenceId);

    void StateUpdated(SupervisorExecutionState state);
}

public sealed class InvestigationObservabilityStore : IInvestigationObserver
{
    private readonly ConcurrentDictionary<Guid, MutableRun> _runs = [];

    public void Start(Guid incidentId, string incidentSummary)
    {
        if (_runs.TryAdd(incidentId, new MutableRun(incidentId, incidentSummary)))
        {
            AddEvent(incidentId, "InvestigationStarted", "Checkout-latency investigation started.");
        }
    }

    public void AgentSelected(Guid incidentId, string agent, string task)
    {
        AddEvent(incidentId, "AgentSelected", $"Supervisor selected {agent}.", agent);
    }

    public void RetrievalStarted(Guid incidentId, string agent, string task, int iteration)
    {
        var run = Required(incidentId);
        lock (run.Gate)
        {
            run.Executions.Add(new MutableExecution(Guid.NewGuid(), agent, task, iteration));
        }

        AddEvent(incidentId, "RetrievalStarted", $"Context retrieval {iteration} started.", agent, iteration);
    }

    public void ContextAssembled(Guid incidentId, string agent, string task, ContextBundle bundle)
    {
        var execution = FindExecution(incidentId, agent, task, bundle.Diagnostics?.RetrievalIteration ?? 1);
        execution.Context = bundle;
        AddEvent(incidentId, "ContextAssembled", $"{bundle.Diagnostics?.DeterministicCandidates ?? 0} candidates considered; {bundle.Evidence.Count} selected.", agent, bundle.Diagnostics?.RetrievalIteration);
    }

    public void AgentStarted(Guid incidentId, string agent, string task, int iteration)
    {
        AddEvent(incidentId, "AgentStarted", $"{agent} started.", agent, iteration);
    }

    public void FindingStored(Guid incidentId, string agent, string task, SpecialistFinding finding, Guid evidenceId)
    {
        var run = Required(incidentId);
        lock (run.Gate)
        {
            var execution = run.Executions.Last(item => item.Agent == agent && item.Task == task && item.CompletedAt is null);
            execution.CompletedAt = DateTimeOffset.UtcNow;
            execution.Status = finding.AdditionalContextRequired is null ? "Completed" : "AdditionalContextRequired";
            execution.FindingSummary = finding.Summary;
            execution.NewEvidenceId = evidenceId;
            execution.AdditionalContext = finding.AdditionalContextRequired;
        }

        AddEvent(incidentId, "FindingStored", $"Finding {evidenceId} stored.", agent);
        if (finding.AdditionalContextRequired is not null)
        {
            AddEvent(incidentId, "AdditionalContextRequested", $"Focused {finding.AdditionalContextRequired.RequiredEvidenceType} context requested.", agent);
        }
    }

    public void StateUpdated(SupervisorExecutionState state)
    {
        var run = Required(state.IncidentId);
        lock (run.Gate)
        {
            run.State = new SupervisorStateView(state.IncidentId, state.CurrentGoal, state.Status.ToString(), state.CompletedInvestigations.ToArray(), state.PendingInvestigations.ToArray(), state.ActiveHypotheses.ToArray(), state.EvidenceReferences.ToArray(), state.Confidence, JsonSerializer.Serialize(state).Length);
        }

        AddEvent(state.IncidentId, "SupervisorStateUpdated", "Compact Supervisor state updated.");
    }

    public void Complete(Guid incidentId, FinalRecommendationView recommendation)
    {
        var run = Required(incidentId);
        lock (run.Gate)
        {
            run.Status = "Completed";
            run.Recommendation = recommendation;
        }

        AddEvent(incidentId, "InvestigationCompleted", "Root-cause recommendation produced.");
    }

    public void Fail(Guid incidentId, string safeMessage)
    {
        var run = Required(incidentId);
        lock (run.Gate)
        {
            run.Status = "Failed";
            run.FailureMessage = safeMessage;
        }

        AddEvent(incidentId, "InvestigationFailed", safeMessage);
    }

    public async Task<InvestigationView?> GetAsync(Guid incidentId, IEvidenceRepository repository, CancellationToken cancellationToken)
    {
        if (!_runs.TryGetValue(incidentId, out var run))
        {
            return null;
        }

        var records = await repository.QueryAsync(incidentId, null, null, 100, cancellationToken);
        lock (run.Gate)
        {
            var executions = run.Executions.Select(item => new AgentExecutionView(item.Id, item.Agent, item.Task, item.Iteration, item.StartedAt, item.CompletedAt, item.Status, item.Context, item.FindingSummary, item.NewEvidenceId, item.AdditionalContext)).ToArray();
            var evidenceRounds = executions
                .Where(item => item.NewEvidenceId is not null)
                .Select((item, index) => new { EvidenceId = item.NewEvidenceId!.Value, Round = index + 1 })
                .ToDictionary(item => item.EvidenceId, item => item.Round);
            var evidence = records.Select(item => new EvidenceSummaryView(item.EvidenceId, item.SourceAgent, item.EvidenceType, item.Component, item.ObservedFrom, item.ObservedTo, item.Summary, item.Importance, item.CreatedAt, item.Summary.Length + item.DetailedContent.Length, evidenceRounds.GetValueOrDefault(item.EvidenceId))).ToArray();
            var currentContext = executions.LastOrDefault()?.ContextBundle;
            return new InvestigationView(run.Id, run.Summary, run.Status, run.State, run.Timeline.ToArray(), executions, evidence, new InvestigationMetricsView(evidence.Length, evidence.Sum(item => item.ApproximateCharacters), run.State?.ApproximateCharacters ?? 0, currentContext?.Evidence.Count ?? 0, currentContext?.Budget?.ApproximateCharacters ?? 0), run.Recommendation, run.FailureMessage);
        }
    }

    private void AddEvent(Guid incidentId, string type, string message, string? agent = null, int? iteration = null)
    {
        var run = Required(incidentId);
        lock (run.Gate)
        {
            run.Timeline.Add(new InvestigationTimelineEvent(run.Timeline.Count + 1, DateTimeOffset.UtcNow, type, message, agent, iteration));
        }
    }

    private MutableRun Required(Guid id) => _runs.TryGetValue(id, out var run) ? run : throw new InvalidOperationException($"Unknown investigation {id}.");

    private MutableExecution FindExecution(Guid id, string agent, string task, int iteration)
    {
        var run = Required(id);
        lock (run.Gate)
        {
            return run.Executions.Last(item => item.Agent == agent && item.Task == task && item.Iteration == iteration);
        }
    }

    private sealed class MutableRun(Guid id, string summary)
    {
        public object Gate { get; } = new();
        public Guid Id { get; } = id;
        public string Summary { get; } = summary;
        public string Status { get; set; } = "Running";
        public SupervisorStateView? State { get; set; }
        public List<InvestigationTimelineEvent> Timeline { get; } = [];
        public List<MutableExecution> Executions { get; } = [];
        public FinalRecommendationView? Recommendation { get; set; }
        public string? FailureMessage { get; set; }
    }

    private sealed class MutableExecution(Guid id, string agent, string task, int iteration)
    {
        public Guid Id { get; } = id;
        public string Agent { get; } = agent;
        public string Task { get; } = task;
        public int Iteration { get; } = iteration;
        public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? CompletedAt { get; set; }
        public string Status { get; set; } = "RetrievingContext";
        public ContextBundle? Context { get; set; }
        public string? FindingSummary { get; set; }
        public Guid? NewEvidenceId { get; set; }
        public AdditionalContextRequirement? AdditionalContext { get; set; }
    }
}
