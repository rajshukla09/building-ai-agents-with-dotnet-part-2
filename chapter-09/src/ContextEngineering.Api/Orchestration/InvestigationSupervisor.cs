using ContextEngineering.Api.Agents;
using ContextEngineering.Api.Context;
using ContextEngineering.Api.Models;
using ContextEngineering.Api.Observability;
using ContextEngineering.Api.Persistence;
using Microsoft.Extensions.Options;

namespace ContextEngineering.Api.Orchestration;

public sealed class InvestigationOptions
{
    public int MaximumSpecialistInvocations { get; set; } = 8;

    public int MaximumRetrievalIterationsPerTask { get; set; } = 2;

    public int MaximumInvestigationRounds { get; set; } = 8;

    public int RetrievalTimeoutSeconds { get; set; } = 10;
}

public interface IInvestigationSupervisor
{
    Task<SupervisorExecutionState> InvestigateAsync(
        Guid incidentId,
        string currentGoal,
        IReadOnlyList<InvestigationAssignment> assignments,
        CancellationToken cancellationToken);
}

public sealed class InvestigationSupervisor(
    IEnumerable<ISpecialistAgent> specialists,
    IContextService contextService,
    IEvidenceRepository evidenceRepository,
    IOptions<InvestigationOptions> options,
    IAdditionalContextValidator? additionalContextValidator = null,
    IInvestigationObserver? observer = null) : IInvestigationSupervisor
{
    private readonly IReadOnlyDictionary<string, ISpecialistAgent> _specialists = specialists
        .ToDictionary(agent => agent.Name, StringComparer.Ordinal);

    public async Task<SupervisorExecutionState> InvestigateAsync(
        Guid incidentId,
        string currentGoal,
        IReadOnlyList<InvestigationAssignment> assignments,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentGoal);

        var state = new SupervisorExecutionState
        {
            IncidentId = incidentId,
            CurrentGoal = currentGoal,
            IncidentSummary = $"Incident {incidentId}: {currentGoal}",
            Status = InvestigationStatus.Running
        };
        observer?.Start(incidentId, state.IncidentSummary);

        state.PendingInvestigations.AddRange(assignments.Select(item => item.TargetAgent));
        state.AffectedComponents.AddRange(assignments
            .Select(item => item.Component)
            .Where(component => !string.IsNullOrWhiteSpace(component))
            .Select(component => component!)
            .Distinct(StringComparer.OrdinalIgnoreCase));
        state.IncidentFrom = assignments
            .Where(item => item.From is not null)
            .Select(item => item.From)
            .Min();
        state.IncidentTo = assignments
            .Where(item => item.To is not null)
            .Select(item => item.To)
            .Max();
        var maximumInvocations = Math.Max(0, options.Value.MaximumSpecialistInvocations);
        var specialistInvocations = 0;

        foreach (var assignment in assignments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (specialistInvocations >= maximumInvocations ||
                state.CompletedInvestigations.Count >= options.Value.MaximumInvestigationRounds)
            {
                state.Status = InvestigationStatus.LimitReached;
                return state;
            }

            if (!_specialists.TryGetValue(assignment.TargetAgent, out var specialist))
            {
                throw new InvalidOperationException(
                    $"Specialist '{assignment.TargetAgent}' is not registered.");
            }

            observer?.AgentSelected(incidentId, assignment.TargetAgent, assignment.Task);

            specialistInvocations += await ExecuteAssignmentAsync(
                state,
                assignment,
                specialist,
                currentGoal,
                maximumInvocations - specialistInvocations,
                cancellationToken);

            if (state.Status == InvestigationStatus.LimitReached)
            {
                return state;
            }
        }

        state.Status = InvestigationStatus.Completed;
        observer?.StateUpdated(state);
        return state;
    }

    private async Task<int> ExecuteAssignmentAsync(
        SupervisorExecutionState state,
        InvestigationAssignment assignment,
        ISpecialistAgent specialist,
        string currentGoal,
        int remainingInvocations,
        CancellationToken cancellationToken)
    {
        var maximumIterations = Math.Max(1, options.Value.MaximumRetrievalIterationsPerTask);
        var requestTypes = assignment.AllowedEvidenceTypes;
        var component = assignment.Component;
        var from = assignment.From;
        var to = assignment.To;
        var priorSelections = new HashSet<string>(StringComparer.Ordinal);
        var selectedEvidenceIds = new HashSet<Guid>();
        var finalStatus = RetrievalAttemptStatus.Sufficient;
        var iterations = 0;
        var invocations = 0;

        for (var iteration = 1; iteration <= maximumIterations; iteration++)
        {
            iterations = iteration;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.Value.RetrievalTimeoutSeconds)));

            ContextBundle context;
            try
            {
                observer?.RetrievalStarted(
                    state.IncidentId,
                    assignment.TargetAgent,
                    assignment.Task,
                    iteration);
                context = await contextService.GetContextAsync(
                    new ContextRequest(
                        state.IncidentId,
                        assignment.TargetAgent,
                        assignment.Task,
                        requestTypes,
                        component,
                        ExecutionState: state,
                        From: from,
                        To: to,
                        SourceAgents: assignment.SourceAgents,
                        RetrievalIteration: iteration),
                    timeout.Token);
                observer?.ContextAssembled(
                    state.IncidentId,
                    assignment.TargetAgent,
                    assignment.Task,
                    context);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                finalStatus = RetrievalAttemptStatus.TimedOut;
                break;
            }

            var selectionKey = string.Join(',', context.EvidenceIds.Order());
            if (!priorSelections.Add(selectionKey))
            {
                finalStatus = RetrievalAttemptStatus.DuplicateRetrievalStopped;
                break;
            }

            foreach (var evidenceId in context.EvidenceIds)
            {
                selectedEvidenceIds.Add(evidenceId);
            }

            if (invocations >= remainingInvocations)
            {
                finalStatus = RetrievalAttemptStatus.IterationLimitReached;
                state.Status = InvestigationStatus.LimitReached;
                break;
            }

            SpecialistFinding finding;
            try
            {
                invocations++;
                observer?.AgentStarted(
                    state.IncidentId,
                    assignment.TargetAgent,
                    assignment.Task,
                    iteration);
                finding = await specialist.InvestigateAsync(
                    new SpecialistRequest(
                        state.IncidentId,
                        assignment.Task,
                        context,
                        new Dictionary<string, string>
                        {
                            ["currentGoal"] = currentGoal,
                            ["retrievalIteration"] = iteration.ToString()
                        }),
                    timeout.Token);

                var evidence = ToEvidence(state.IncidentId, finding);
                await evidenceRepository.AddAsync(evidence, timeout.Token);
                UpdateCompactState(state, assignment, finding, evidence);
                observer?.FindingStored(
                    state.IncidentId,
                    assignment.TargetAgent,
                    assignment.Task,
                    finding,
                    evidence.EvidenceId);
                observer?.StateUpdated(state);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                finalStatus = RetrievalAttemptStatus.TimedOut;
                break;
            }

            if (finding.AdditionalContextRequired is null)
            {
                finalStatus = iteration == 1
                    ? RetrievalAttemptStatus.Sufficient
                    : RetrievalAttemptStatus.AdditionalContextProvided;
                break;
            }

            if (iteration == maximumIterations)
            {
                finalStatus = RetrievalAttemptStatus.IterationLimitReached;
                break;
            }

            var validator = additionalContextValidator ??
                new AdditionalContextValidator(new AgentRetrievalProfileProvider());
            var validation = validator.Validate(
                assignment.TargetAgent,
                finding.AdditionalContextRequired);
            if (!validation.IsValid)
            {
                finalStatus = RetrievalAttemptStatus.InvalidRequest;
                break;
            }

            requestTypes = [finding.AdditionalContextRequired.RequiredEvidenceType];
            component = finding.AdditionalContextRequired.Component;
            from = finding.AdditionalContextRequired.From;
            to = finding.AdditionalContextRequired.To;
        }

        state.RetrievalOutcomes.Add(new CompactRetrievalOutcome(
            assignment.TargetAgent,
            iterations,
            finalStatus,
            selectedEvidenceIds.ToArray()));

        return invocations;
    }

    private static EvidenceRecord ToEvidence(Guid incidentId, SpecialistFinding finding)
    {
        return new EvidenceRecord
        {
            EvidenceId = Guid.NewGuid(),
            IncidentId = incidentId,
            SourceAgent = finding.SourceAgent,
            EvidenceType = finding.EvidenceType,
            Component = finding.Component,
            ObservedFrom = finding.ObservedFrom,
            ObservedTo = finding.ObservedTo,
            Summary = finding.Summary,
            DetailedContent = finding.DetailedContent,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static void UpdateCompactState(
        SupervisorExecutionState state,
        InvestigationAssignment assignment,
        SpecialistFinding finding,
        EvidenceRecord evidence)
    {
        state.PendingInvestigations.Remove(assignment.TargetAgent);
        if (!state.CompletedInvestigations.Contains(assignment.TargetAgent, StringComparer.Ordinal))
        {
            state.CompletedInvestigations.Add(assignment.TargetAgent);
        }
        state.EvidenceReferences.Add(new EvidenceReference(
            evidence.EvidenceId,
            evidence.SourceAgent,
            evidence.EvidenceType,
            evidence.Summary));

        if (!string.IsNullOrWhiteSpace(finding.Hypothesis))
        {
            state.ActiveHypotheses.Add(new ActiveHypothesis(
                finding.Hypothesis,
                finding.Confidence,
                [evidence.EvidenceId]));
        }

        state.Confidence = state.ActiveHypotheses.Count == 0
            ? 0
            : state.ActiveHypotheses.Average(item => item.Confidence);
    }
}
