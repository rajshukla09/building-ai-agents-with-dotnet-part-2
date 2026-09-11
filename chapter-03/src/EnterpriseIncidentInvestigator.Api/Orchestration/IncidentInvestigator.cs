using EnterpriseIncidentInvestigator.Api.Agents;
using EnterpriseIncidentInvestigator.Api.Configuration;
using EnterpriseIncidentInvestigator.Api.Contracts;
using EnterpriseIncidentInvestigator.Api.Observability;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace EnterpriseIncidentInvestigator.Api.Orchestration;

public interface IIncidentInvestigator
{
    Task<InvestigationResponse> InvestigateAsync(
        string incident,
        Guid? runId = null,
        CancellationToken cancellationToken = default);
}

public sealed class IncidentInvestigator(
    IEnumerable<ISpecializedAgent> agents,
    ISupervisorModel supervisor,
    IRootCauseAgent rootCause,
    IInvestigationEventStore events,
    IOptions<InvestigationOptions> options,
    ILogger<IncidentInvestigator> logger) : IIncidentInvestigator
{
    public async Task<InvestigationResponse> InvestigateAsync(
        string incident,
        Guid? requestedRunId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(incident))
        {
            throw new ArgumentException(
                "An incident is required.",
                nameof(incident));
        }

        var runId = requestedRunId.GetValueOrDefault() == Guid.Empty
            ? Guid.NewGuid()
            : requestedRunId.Value;

        using var investigationActivity =
            InvestigationTelemetry.Source.StartActivity("IncidentInvestigation");

        investigationActivity?.SetTag("investigation.run_id", runId);

        await events.CreateAsync(
            runId,
            incident,
            cancellationToken);

        var context = new IncidentInvestigationContext(incident);
        var gate = new object();

        var limitReached = false;
        var reservedInvocations = 0;

        Exception? fatal = null;

        var delegates = agents
            .Select(agent => new AgentDelegate(
                agent.Descriptor,
                async (assignment, reason, ct) =>
                {
                    int sequence;

                    lock (gate)
                    {
                        if (reservedInvocations >= options.Value.MaxAgentInvocations)
                        {
                            limitReached = true;

                            throw new AgentInvocationLimitException(
                                options.Value.MaxAgentInvocations);
                        }

                        sequence = ++reservedInvocations;
                    }

                    string inputSummary;

                    lock (gate)
                    {
                        inputSummary = Describe(context);
                    }

                    using var agentActivity =
                        InvestigationTelemetry.Source.StartActivity(
                            agent.Descriptor.Name);

                    agentActivity?.SetTag(
                        "investigation.run_id",
                        runId);

                    agentActivity?.SetTag(
                        "agent.sequence",
                        sequence);

                    agentActivity?.SetTag(
                        "agent.reason_selected",
                        reason);

                    await events.AppendAsync(
                        runId,
                        new(
                            InvestigationEventTypes.SupervisorDecision,
                            "Selected",
                            agent.Descriptor.Name,
                            sequence,
                            Reason: reason,
                            InputContextSummary: inputSummary),
                        ct);

                    await events.AppendAsync(
                        runId,
                        new(
                            InvestigationEventTypes.AgentStarted,
                            "Running",
                            agent.Descriptor.Name,
                            sequence,
                            InputContextSummary: inputSummary),
                        ct);

                    if (agent.Descriptor.McpServer is not null)
                    {
                        await events.AppendAsync(
                            runId,
                            new(
                                InvestigationEventTypes.McpCapabilitiesDiscovered,
                                "Available",
                                agent.Descriptor.Name,
                                sequence,
                                agent.Descriptor.McpServer,
                                Reason: string.Join(
                                    ",",
                                    agent.Descriptor.McpTools ?? [])),
                            ct);
                    }

                    var watch = Stopwatch.StartNew();
                    var succeeded = false;

                    try
                    {
                        var finding = await agent.InvestigateAsync(
                            context,
                            assignment,
                            ct);

                        succeeded = true;
                        watch.Stop();

                        lock (gate)
                        {
                            context.Add(
                                finding,
                                new(
                                    sequence,
                                    agent.Descriptor.Name,
                                    watch.ElapsedMilliseconds,
                                    true,
                                    reason));
                        }

                        foreach (var tool in finding.ToolInvocations)
                        {
                            await events.AppendAsync(
                                runId,
                                new(
                                    InvestigationEventTypes.ToolCompleted,
                                    tool.Succeeded ? "Completed" : "Failed",
                                    agent.Descriptor.Name,
                                    sequence,
                                    tool.Server,
                                    tool.Tool,
                                    tool.Sequence,
                                    DurationMilliseconds:
                                        tool.DurationMilliseconds,
                                    ErrorSummary:
                                        tool.ErrorSummary),
                                ct);
                        }

                        await events.AppendAsync(
                            runId,
                            new(
                                InvestigationEventTypes.AgentCompleted,
                                "Completed",
                                agent.Descriptor.Name,
                                sequence,
                                FindingSummary: finding.Summary,
                                SuggestedNextInvestigation:
                                    finding.SuggestedNextInvestigation,
                                Confidence: finding.Confidence,
                                DurationMilliseconds:
                                    watch.ElapsedMilliseconds),
                            ct);

                        return finding;
                    }
                    catch (Exception exception)
                        when (exception is not AgentInvocationLimitException)
                    {
                        fatal = exception;

                        agentActivity?.SetStatus(
                            ActivityStatusCode.Error,
                            exception.Message);

                        watch.Stop();

                        lock (gate)
                        {
                            context.Add(
                                new(
                                    agent.Descriptor.Name,
                                    $"Investigation failed: {exception.Message}",
                                    [],
                                    0,
                                    null,
                                    []),
                                new(
                                    sequence,
                                    agent.Descriptor.Name,
                                    watch.ElapsedMilliseconds,
                                    false,
                                    reason));
                        }

                        await events.AppendAsync(
                            runId,
                            new(
                                InvestigationEventTypes.AgentFailed,
                                "Failed",
                                agent.Descriptor.Name,
                                sequence,
                                DurationMilliseconds:
                                    watch.ElapsedMilliseconds,
                                ErrorSummary:
                                    exception.Message),
                            ct);

                        throw;
                    }
                    finally
                    {
                        watch.Stop();

                        logger.LogInformation(
                            "Agent {Agent} invocation {Sequence} completed; " +
                            "succeeded={Succeeded}; reason={Reason}",
                            agent.Descriptor.Name,
                            sequence,
                            succeeded,
                            reason);
                    }
                }))
            .ToArray();

        try
        {
            await supervisor.RunAsync(
                incident,
                Describe(context),
                delegates,
                cancellationToken);
        }
        catch (AgentInvocationLimitException)
        {
            limitReached = true;

            await events.AppendAsync(
                runId,
                new(
                    InvestigationEventTypes.LimitReached,
                    "LimitReached",
                    Reason:
                        $"Maximum of {options.Value.MaxAgentInvocations} " +
                        "agent invocations reached."),
                cancellationToken);
        }
        catch (Exception exception)
        {
            fatal = exception;

            logger.LogError(
                exception,
                "Fatal supervisor or agent failure");
        }

        RootCauseConclusion conclusion;

        try
        {
            conclusion = await rootCause.SynthesizeAsync(
                context,
                limitReached,
                cancellationToken);

            await events.AppendAsync(
                runId,
                new(
                    InvestigationEventTypes.RootCauseGenerated,
                    "Completed",
                    "RootCauseAgent",
                    FindingSummary: conclusion.RootCause,
                    Confidence: conclusion.Confidence),
                cancellationToken);
        }
        catch (Exception exception)
        {
            fatal = exception;

            conclusion = new(
                "Final synthesis failed.",
                "Unable to synthesize the available evidence.",
                0,
                "Review the persisted findings and retry synthesis.");

            await events.AppendAsync(
                runId,
                new(
                    InvestigationEventTypes.Failed,
                    "Failed",
                    "RootCauseAgent",
                    ErrorSummary: exception.Message),
                cancellationToken);
        }

        var reason = fatal is not null
            ? "Fatal failure; best available evidence synthesized."
            : limitReached
                ? "Maximum agent invocations reached; best available evidence synthesized."
                : "Supervisor declared the available evidence sufficient.";

        await events.AppendAsync(
            runId,
            new(
                fatal is null
                    ? InvestigationEventTypes.Completed
                    : InvestigationEventTypes.Failed,
                fatal is null
                    ? limitReached
                        ? "LimitReached"
                        : "Completed"
                    : "Failed",
                Reason: reason,
                ErrorSummary: fatal?.Message),
            cancellationToken);

        var response = new InvestigationResponse(
            conclusion.Summary,
            conclusion.RootCause,
            conclusion.Confidence,
            conclusion.Recommendation,
            context.AgentInvocations,
            context.Findings,
            limitReached,
            reason,
            runId);

        await events.CompleteAsync(
            runId,
            fatal is not null
                ? "Failed"
                : limitReached
                    ? "LimitReached"
                    : "Completed",
            response,
            cancellationToken);

        return response;
    }

    private static string Describe(IncidentInvestigationContext context)
    {
        if (context.Findings.Count == 0)
        {
            return $"Incident: {context.OriginalIncident}; no findings yet.";
        }

        return string.Join(
            "\n",
            context.Findings.Select(
                (finding, index) =>
                    $"{index + 1}. {finding.AgentName}: " +
                    $"{finding.Summary}; " +
                    $"suggested: {finding.SuggestedNextInvestigation}"));
    }
}

public sealed class AgentInvocationLimitException(int limit)
    : InvalidOperationException(
        $"The agent invocation limit of {limit} was reached.");
