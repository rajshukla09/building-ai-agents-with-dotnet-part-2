using System.Text.Json;
using ContextEngineering.Api.Agents;
using ContextEngineering.Api.Context;
using ContextEngineering.Api.Models;
using ContextEngineering.Api.Orchestration;
using ContextEngineering.Api.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace ContextEngineering.Tests;

public sealed class ContextArchitectureTests
{
    [Fact]
    public async Task Full_specialist_findings_are_not_copied_into_supervisor_state()
    {
        await using var harness = await TestHarness.CreateAsync();
        const string detailedContent = "DISTINCTIVE-FULL-FINDING: stack frames and raw diagnostic payload";
        var agent = new RecordingAgent(nameof(ApplicationAgent), EvidenceType.Application, detailedContent);
        var supervisor = harness.CreateSupervisor([agent]);

        var state = await supervisor.InvestigateAsync(
            Guid.NewGuid(),
            "Explain the checkout latency increase.",
            [new InvestigationAssignment(agent.Name, "Inspect the application path.")],
            default);

        var serializedState = JsonSerializer.Serialize(state);

        Assert.DoesNotContain(detailedContent, serializedState);
        Assert.Single(state.EvidenceReferences);
        Assert.Single(state.ActiveHypotheses);
    }

    [Fact]
    public async Task Evidence_is_persisted_separately_from_execution_state()
    {
        await using var harness = await TestHarness.CreateAsync();
        var incidentId = Guid.NewGuid();
        var agent = new RecordingAgent(
            nameof(LogInvestigationAgent),
            EvidenceType.Log,
            "Raw log sequence for request checkout-42.");
        var supervisor = harness.CreateSupervisor([agent]);

        var state = await supervisor.InvestigateAsync(
            incidentId,
            "Find the first latency signal.",
            [new InvestigationAssignment(agent.Name, "Inspect checkout logs.")],
            default);
        var evidence = await harness.Repository.QueryAsync(
            incidentId,
            null,
            null,
            20,
            default);

        var stored = Assert.Single(evidence);
        Assert.Equal("Raw log sequence for request checkout-42.", stored.DetailedContent);
        Assert.Equal(stored.EvidenceId, Assert.Single(state.EvidenceReferences).EvidenceId);
    }

    [Fact]
    public async Task Context_service_returns_only_incident_specific_evidence()
    {
        await using var harness = await TestHarness.CreateAsync();
        var requestedIncident = Guid.NewGuid();
        var unrelatedIncident = Guid.NewGuid();
        await harness.AddEvidenceAsync(requestedIncident, EvidenceType.Deployment, "matching");
        await harness.AddEvidenceAsync(unrelatedIncident, EvidenceType.Deployment, "unrelated");

        var bundle = await harness.ContextService.GetContextAsync(
            new ContextRequest(
                requestedIncident,
                nameof(DeploymentAgent),
                "Correlate the deployment with onset."),
            default);

        var item = Assert.Single(bundle.Evidence);
        Assert.Equal("matching", item.DetailedContent);
        Assert.Equal(requestedIncident, bundle.IncidentId);
    }

    [Fact]
    public async Task Different_specialist_requests_receive_different_context()
    {
        await using var harness = await TestHarness.CreateAsync();
        var incidentId = Guid.NewGuid();
        await harness.AddEvidenceAsync(incidentId, EvidenceType.Deployment, "release v4.8");
        await harness.AddEvidenceAsync(incidentId, EvidenceType.Database, "slow query");

        var deploymentContext = await harness.ContextService.GetContextAsync(
            new ContextRequest(
                incidentId,
                nameof(DeploymentAgent),
                "Inspect release timing.",
                [EvidenceType.Deployment]),
            default);
        var databaseContext = await harness.ContextService.GetContextAsync(
            new ContextRequest(
                incidentId,
                nameof(DatabaseAgent),
                "Inspect database latency.",
                [EvidenceType.Database]),
            default);

        Assert.Equal(EvidenceType.Deployment, Assert.Single(deploymentContext.Evidence).EvidenceType);
        Assert.Equal(EvidenceType.Database, Assert.Single(databaseContext.Evidence).EvidenceType);
        Assert.NotEqual(deploymentContext.CurrentGoal, databaseContext.CurrentGoal);
    }

    [Fact]
    public async Task Specialist_calls_depend_only_on_explicit_request_not_conversation_history()
    {
        var specialist = new ApplicationAgent();
        var incidentId = Guid.NewGuid();
        var context = new ContextBundle(
            incidentId,
            specialist.Name,
            "Inspect code paths.",
            []);
        var request = new SpecialistRequest(
            incidentId,
            "Inspect code paths.",
            context,
            new Dictionary<string, string>());

        var first = await specialist.InvestigateAsync(request, default);
        var second = await specialist.InvestigateAsync(request, default);

        Assert.Equal(first.Summary, second.Summary);
        Assert.Equal(first.DetailedContent, second.DetailedContent);
        Assert.Empty(typeof(ApplicationAgent).GetFields(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public));
    }

    [Fact]
    public async Task Supervisor_respects_invocation_limit()
    {
        await using var harness = await TestHarness.CreateAsync();
        var firstAgent = new RecordingAgent(
            nameof(LogInvestigationAgent),
            EvidenceType.Log,
            "first detail");
        var secondAgent = new RecordingAgent(
            nameof(DatabaseAgent),
            EvidenceType.Database,
            "second detail");
        var supervisor = harness.CreateSupervisor([firstAgent, secondAgent], maximumInvocations: 1);

        var state = await supervisor.InvestigateAsync(
            Guid.NewGuid(),
            "Investigate latency.",
            [
                new InvestigationAssignment(firstAgent.Name, "First task."),
                new InvestigationAssignment(secondAgent.Name, "Second task.")
            ],
            default);

        Assert.Equal(InvestigationStatus.LimitReached, state.Status);
        Assert.Single(state.CompletedInvestigations);
        Assert.Equal(0, secondAgent.InvocationCount);
    }

    [Fact]
    public async Task Supervisor_propagates_cancellation_without_invoking_specialist()
    {
        await using var harness = await TestHarness.CreateAsync();
        var agent = new RecordingAgent(nameof(DatabaseAgent), EvidenceType.Database, "detail");
        var supervisor = harness.CreateSupervisor([agent]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            supervisor.InvestigateAsync(
                Guid.NewGuid(),
                "Investigate latency.",
                [new InvestigationAssignment(agent.Name, "Inspect database.")],
                cancellation.Token));

        Assert.Equal(0, agent.InvocationCount);
    }

    [Fact]
    public async Task Log_and_database_profiles_select_different_context_for_same_incident()
    {
        await using var harness = await TestHarness.CreateAsync();
        var incidentId = Guid.NewGuid();
        await harness.AddEvidenceAsync(
            incidentId,
            EvidenceType.Deployment,
            "release v4.8",
            nameof(DeploymentAgent));
        await harness.AddEvidenceAsync(
            incidentId,
            EvidenceType.Database,
            "connection pool saturation",
            nameof(DatabaseAgent));

        var logBundle = await harness.ContextService.GetContextAsync(
            new ContextRequest(incidentId, nameof(LogInvestigationAgent), "Correlate log errors."),
            default);
        var databaseBundle = await harness.ContextService.GetContextAsync(
            new ContextRequest(incidentId, nameof(DatabaseAgent), "Inspect database latency."),
            default);

        Assert.Contains(logBundle.Evidence, item => item.EvidenceType == EvidenceType.Deployment);
        Assert.DoesNotContain(logBundle.Evidence, item => item.EvidenceType == EvidenceType.Database);
        Assert.Contains(databaseBundle.Evidence, item => item.EvidenceType == EvidenceType.Database);
        Assert.NotEqual(
            logBundle.Evidence.Select(item => item.EvidenceId).Order(),
            databaseBundle.Evidence.Select(item => item.EvidenceId).Order());
    }

    [Fact]
    public async Task Time_range_changes_selected_evidence()
    {
        await using var harness = await TestHarness.CreateAsync();
        var incidentId = Guid.NewGuid();
        var incidentStart = DateTimeOffset.UtcNow.AddHours(-1);
        await harness.AddEvidenceAsync(
            incidentId,
            EvidenceType.Log,
            "inside window",
            nameof(LogInvestigationAgent),
            observedFrom: incidentStart.AddMinutes(10),
            observedTo: incidentStart.AddMinutes(20));
        await harness.AddEvidenceAsync(
            incidentId,
            EvidenceType.Log,
            "outside window",
            nameof(LogInvestigationAgent),
            observedFrom: incidentStart.AddHours(-3),
            observedTo: incidentStart.AddHours(-2));

        var bundle = await harness.ContextService.GetContextAsync(
            new ContextRequest(
                incidentId,
                nameof(LogInvestigationAgent),
                "Inspect incident-window logs.",
                From: incidentStart,
                To: incidentStart.AddHours(1)),
            default);

        Assert.Equal("inside window", Assert.Single(bundle.Evidence).DetailedContent);
    }

    [Fact]
    public async Task Component_filter_excludes_other_services()
    {
        await using var harness = await TestHarness.CreateAsync();
        var incidentId = Guid.NewGuid();
        await harness.AddEvidenceAsync(
            incidentId,
            EvidenceType.Application,
            "checkout evidence",
            nameof(ApplicationAgent),
            "checkout-api");
        await harness.AddEvidenceAsync(
            incidentId,
            EvidenceType.Application,
            "catalog evidence",
            nameof(ApplicationAgent),
            "catalog-api");

        var bundle = await harness.ContextService.GetContextAsync(
            new ContextRequest(
                incidentId,
                nameof(ApplicationAgent),
                "Inspect checkout code.",
                Component: "checkout-api"),
            default);

        Assert.Equal("checkout-api", Assert.Single(bundle.Evidence).Component);
    }

    [Fact]
    public async Task Profile_and_explicit_source_filters_exclude_unrelated_evidence()
    {
        await using var harness = await TestHarness.CreateAsync();
        var incidentId = Guid.NewGuid();
        await harness.AddEvidenceAsync(
            incidentId,
            EvidenceType.Telemetry,
            "large unrelated telemetry",
            nameof(InfrastructureTelemetryAgent));
        await harness.AddEvidenceAsync(
            incidentId,
            EvidenceType.Log,
            "database timeout signal",
            nameof(LogInvestigationAgent));

        var bundle = await harness.ContextService.GetContextAsync(
            new ContextRequest(
                incidentId,
                nameof(DatabaseAgent),
                "Follow database timeout signals.",
                SourceAgents: [nameof(LogInvestigationAgent)]),
            default);

        var evidence = Assert.Single(bundle.Evidence);
        Assert.Equal(nameof(LogInvestigationAgent), evidence.SourceAgent);
        Assert.DoesNotContain(bundle.Evidence, item => item.EvidenceType == EvidenceType.Telemetry);
        Assert.Equal(
            [EvidenceType.Database, EvidenceType.Log, EvidenceType.Application, EvidenceType.Deployment],
            bundle.RetrievalSpecification!.EvidenceTypes);
    }

    [Fact]
    public async Task Context_bundle_exposes_ids_provenance_and_retrieval_reason()
    {
        await using var harness = await TestHarness.CreateAsync();
        var incidentId = Guid.NewGuid();
        await harness.AddEvidenceAsync(
            incidentId,
            EvidenceType.Deployment,
            "deployment evidence",
            nameof(DeploymentAgent));

        var bundle = await harness.ContextService.GetContextAsync(
            new ContextRequest(incidentId, nameof(LogInvestigationAgent), "Correlate deployment."),
            default);

        var evidence = Assert.Single(bundle.Evidence);
        Assert.Equal(evidence.EvidenceId, Assert.Single(bundle.EvidenceIds));
        Assert.Equal(nameof(DeploymentAgent), evidence.SourceAgent);
        Assert.False(string.IsNullOrWhiteSpace(evidence.RetrievalReason));
        Assert.Equal(nameof(LogInvestigationAgent), bundle.RetrievalSpecification!.TargetAgent);
    }

    [Fact]
    public async Task Large_unrelated_evidence_never_enters_agent_context()
    {
        await using var harness = await TestHarness.CreateAsync();
        var incidentId = Guid.NewGuid();
        await harness.AddEvidenceAsync(
            incidentId,
            EvidenceType.Telemetry,
            new string('X', 50_000),
            nameof(InfrastructureTelemetryAgent));
        await harness.AddEvidenceAsync(
            incidentId,
            EvidenceType.Database,
            "relevant database evidence",
            nameof(DatabaseAgent));

        var bundle = await harness.ContextService.GetContextAsync(
            new ContextRequest(incidentId, nameof(DatabaseAgent), "Inspect database latency."),
            default);

        var evidence = Assert.Single(bundle.Evidence);
        Assert.Equal(EvidenceType.Database, evidence.EvidenceType);
        Assert.Equal("relevant database evidence", evidence.DetailedContent);
    }

    [Fact]
    public void Ranking_prefers_component_time_and_profile_relevance_deterministically()
    {
        var incidentId = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-08-28T10:00:00Z");
        var preferred = Evidence(
            incidentId,
            EvidenceType.Database,
            "orders-database",
            "connection waits during checkout",
            now.AddMinutes(-10),
            now);
        var unrelated = Evidence(
            incidentId,
            EvidenceType.Application,
            "catalog-api",
            "catalog cache refresh",
            now.AddDays(-2),
            now.AddDays(-2));
        var specification = new RetrievalSpecification(
            incidentId,
            nameof(DatabaseAgent),
            "Investigate checkout connection waits.",
            [EvidenceType.Database, EvidenceType.Log, EvidenceType.Application],
            [nameof(DatabaseAgent), nameof(LogInvestigationAgent), nameof(ApplicationAgent)],
            ["orders-database"],
            now.AddHours(-1),
            now,
            10,
            5_000,
            "Database profile");
        var ranker = new ExplainableEvidenceRanker();

        var firstRanking = ranker.Rank([unrelated, preferred], specification, null);
        var secondRanking = ranker.Rank([unrelated, preferred], specification, null);

        Assert.Equal(preferred.EvidenceId, firstRanking[0].Evidence.EvidenceId);
        Assert.Equal(
            firstRanking.Select(item => item.Evidence.EvidenceId),
            secondRanking.Select(item => item.Evidence.EvidenceId));
        Assert.Contains(firstRanking[0].Factors, factor => factor.Name == "component");
        Assert.Contains(firstRanking[0].Factors, factor => factor.Name == "time-window");
        Assert.True(firstRanking[0].Score > firstRanking[1].Score);
    }

    [Fact]
    public void Retrieval_profile_order_changes_ranking_weight()
    {
        var incidentId = Guid.NewGuid();
        var createdAt = DateTimeOffset.Parse("2026-08-28T10:00:00Z");
        var deployment = Evidence(
            incidentId,
            EvidenceType.Deployment,
            "checkout-api",
            "release occurred",
            createdAt,
            createdAt);
        var database = Evidence(
            incidentId,
            EvidenceType.Database,
            "checkout-api",
            "query waits occurred",
            createdAt,
            createdAt);
        var baseSpecification = new RetrievalSpecification(
            incidentId,
            nameof(DatabaseAgent),
            "Investigate latency.",
            [EvidenceType.Database, EvidenceType.Deployment],
            [],
            [],
            null,
            null,
            10,
            5_000,
            "profile");
        var ranker = new ExplainableEvidenceRanker();

        var databaseFirst = ranker.Rank([deployment, database], baseSpecification, null);
        var deploymentFirst = ranker.Rank(
            [deployment, database],
            baseSpecification with
            {
                EvidenceTypes = [EvidenceType.Deployment, EvidenceType.Database]
            },
            null);

        Assert.Equal(database.EvidenceId, databaseFirst[0].Evidence.EvidenceId);
        Assert.Equal(deployment.EvidenceId, deploymentFirst[0].Evidence.EvidenceId);
    }

    [Fact]
    public async Task Budget_retains_highest_ranked_items_and_bounds_relevant_growth()
    {
        await using var harness = await TestHarness.CreateAsync();
        var incidentId = Guid.NewGuid();
        for (var index = 0; index < 75; index++)
        {
            await harness.AddEvidenceAsync(
                incidentId,
                EvidenceType.Database,
                $"database evidence {index}",
                nameof(DatabaseAgent),
                "orders-database",
                summary: $"Database observation {index}",
                importance: index == 42 ? 5 : 1);
        }

        var bundle = await harness.ContextService.GetContextAsync(
            new ContextRequest(
                incidentId,
                nameof(DatabaseAgent),
                "Investigate database evidence 42.",
                Component: "orders-database",
                MaximumItems: 5),
            default);

        Assert.Equal(75, bundle.Diagnostics!.TotalIncidentEvidence);
        Assert.Equal(5, bundle.Evidence.Count);
        Assert.True(bundle.Budget!.ApproximateCharacters <= bundle.Budget.MaximumCharacters);
        Assert.Contains(bundle.Evidence, item => item.DetailedContent == "database evidence 42");
    }

    [Fact]
    public async Task Repeated_findings_are_deduplicated_before_budgeting()
    {
        await using var harness = await TestHarness.CreateAsync();
        var incidentId = Guid.NewGuid();
        for (var index = 0; index < 4; index++)
        {
            await harness.AddEvidenceAsync(
                incidentId,
                EvidenceType.Log,
                $"repeated details {index}",
                nameof(LogInvestigationAgent),
                summary: "Connection pool warning repeated");
        }

        var bundle = await harness.ContextService.GetContextAsync(
            new ContextRequest(incidentId, nameof(LogInvestigationAgent), "Inspect pool warnings."),
            default);

        Assert.Single(bundle.Evidence);
        Assert.Equal(3, bundle.Diagnostics!.DuplicatesRemoved);
    }

    [Fact]
    public async Task Ranking_never_crosses_incident_boundary()
    {
        await using var harness = await TestHarness.CreateAsync();
        var requestedIncident = Guid.NewGuid();
        var otherIncident = Guid.NewGuid();
        await harness.AddEvidenceAsync(
            requestedIncident,
            EvidenceType.Database,
            "low importance matching incident",
            nameof(DatabaseAgent),
            importance: 1);
        await harness.AddEvidenceAsync(
            otherIncident,
            EvidenceType.Database,
            "maximum importance other incident",
            nameof(DatabaseAgent),
            importance: 5);

        var bundle = await harness.ContextService.GetContextAsync(
            new ContextRequest(requestedIncident, nameof(DatabaseAgent), "Investigate database."),
            default);

        Assert.Equal("low importance matching incident", Assert.Single(bundle.Evidence).DetailedContent);
        Assert.Equal(1, bundle.Diagnostics!.TotalIncidentEvidence);
    }

    [Fact]
    public async Task Structured_additional_context_is_validated_and_uses_a_fresh_bundle()
    {
        await using var harness = await TestHarness.CreateAsync();
        var incidentId = Guid.NewGuid();
        await harness.AddEvidenceAsync(
            incidentId,
            EvidenceType.Database,
            "checkout database symptom",
            nameof(DatabaseAgent),
            "checkout-api",
            summary: "Checkout database symptom");
        await harness.AddEvidenceAsync(
            incidentId,
            EvidenceType.Database,
            "orders connection waits",
            nameof(DatabaseAgent),
            "orders-database",
            summary: "Orders connection waits");
        var agent = new AdaptiveRecordingAgent(
            nameof(DatabaseAgent),
            new AdditionalContextRequirement(
                EvidenceType.Database,
                "orders-database",
                null,
                null,
                "Connection wait measurements",
                "Confirm database saturation."));
        var supervisor = harness.CreateSupervisor([agent], maximumRetrievalIterations: 2);

        var state = await supervisor.InvestigateAsync(
            incidentId,
            "Explain checkout latency.",
            [new InvestigationAssignment(agent.Name, "Inspect database symptoms.")],
            default);

        Assert.Equal(2, agent.Contexts.Count);
        Assert.Equal(2, agent.Contexts[0].Evidence.Count);
        Assert.All(
            agent.Contexts[1].Evidence,
            item => Assert.Equal("orders-database", item.Component));
        Assert.DoesNotContain(
            agent.Contexts[1].Evidence,
            item => item.DetailedContent == "checkout database symptom");
        Assert.Equal(
            RetrievalAttemptStatus.AdditionalContextProvided,
            Assert.Single(state.RetrievalOutcomes).Status);
    }

    [Fact]
    public async Task Invalid_and_repeated_additional_retrieval_are_bounded()
    {
        await using var harness = await TestHarness.CreateAsync();
        var incidentId = Guid.NewGuid();
        await harness.AddEvidenceAsync(
            incidentId,
            EvidenceType.Database,
            "database evidence",
            nameof(DatabaseAgent));
        var invalidAgent = new AdaptiveRecordingAgent(
            nameof(DatabaseAgent),
            new AdditionalContextRequirement(
                EvidenceType.Telemetry,
                "checkout-platform",
                null,
                null,
                "Host metrics",
                "Request outside profile."));
        var invalidSupervisor = harness.CreateSupervisor([invalidAgent], maximumRetrievalIterations: 3);

        var invalidState = await invalidSupervisor.InvestigateAsync(
            incidentId,
            "Investigate latency.",
            [new InvestigationAssignment(invalidAgent.Name, "Inspect database.")],
            default);

        Assert.Equal(RetrievalAttemptStatus.InvalidRequest, Assert.Single(invalidState.RetrievalOutcomes).Status);
        Assert.Single(invalidAgent.Contexts);

        var repeatedAgent = new AdaptiveRecordingAgent(
            nameof(DatabaseAgent),
            new AdditionalContextRequirement(
                EvidenceType.Database,
                "checkout-api",
                null,
                null,
                "More database evidence",
                "Retry the same bounded query."),
            alwaysRequest: true);
        var repeatedSupervisor = harness.CreateSupervisor([repeatedAgent], maximumRetrievalIterations: 2);
        var repeatedState = await repeatedSupervisor.InvestigateAsync(
            incidentId,
            "Investigate latency.",
            [new InvestigationAssignment(repeatedAgent.Name, "Inspect database.", Component: "checkout-api")],
            default);

        Assert.Equal(
            RetrievalAttemptStatus.DuplicateRetrievalStopped,
            Assert.Single(repeatedState.RetrievalOutcomes).Status);
        Assert.Single(repeatedAgent.Contexts);
    }

    [Fact]
    public async Task Checkout_investigation_accumulates_evidence_externally_with_distinct_bounded_bundles()
    {
        await using var harness = await TestHarness.CreateAsync();
        var incidentId = Guid.NewGuid();
        var deployment = new ScenarioAgent(
            nameof(DeploymentAgent),
            EvidenceType.Deployment,
            "checkout-api",
            "Release v4.8 completed shortly before latency increased.");
        var logs = new ScenarioAgent(
            nameof(LogInvestigationAgent),
            EvidenceType.Log,
            "checkout-api",
            "Connection-pool exhaustion warnings began after v4.8.");
        var database = new ScenarioAgent(
            nameof(DatabaseAgent),
            EvidenceType.Database,
            "orders-database",
            "Connection acquisition waits increased sixfold.");
        var application = new ScenarioAgent(
            nameof(ApplicationAgent),
            EvidenceType.Application,
            "checkout-api",
            "Release v4.8 reduced the configured database pool size.");
        var supervisor = harness.CreateSupervisor([deployment, logs, database, application]);

        var state = await supervisor.InvestigateAsync(
            incidentId,
            "Explain checkout latency after deployment and recommend mitigation.",
            [
                new InvestigationAssignment(deployment.Name, "Confirm release timing."),
                new InvestigationAssignment(logs.Name, "Correlate warnings with the release."),
                new InvestigationAssignment(database.Name, "Measure connection waits."),
                new InvestigationAssignment(application.Name, "Inspect pool configuration changes.")
            ],
            default);
        var storedEvidence = await harness.Repository.QueryAsync(
            incidentId,
            null,
            null,
            100,
            default);

        Assert.Equal(4, storedEvidence.Count);
        Assert.Equal(4, state.EvidenceReferences.Count);
        Assert.Empty(Assert.Single(deployment.Contexts).Evidence);
        Assert.Contains(Assert.Single(logs.Contexts).Evidence, item =>
            item.EvidenceType == EvidenceType.Deployment);
        Assert.Contains(Assert.Single(database.Contexts).Evidence, item =>
            item.EvidenceType == EvidenceType.Log);
        Assert.Contains(Assert.Single(application.Contexts).Evidence, item =>
            item.EvidenceType == EvidenceType.Database);
        Assert.All(
            new[] { logs, database, application },
            agent => Assert.True(agent.Contexts[0].Budget!.SelectedItems <=
                agent.Contexts[0].Budget!.MaximumItems));
        Assert.DoesNotContain(
            "reduced the configured database pool size",
            JsonSerializer.Serialize(state),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Maximum_adaptive_iterations_produces_compact_limit_outcome()
    {
        await using var harness = await TestHarness.CreateAsync();
        var incidentId = Guid.NewGuid();
        await harness.AddEvidenceAsync(
            incidentId,
            EvidenceType.Database,
            "checkout symptom",
            nameof(DatabaseAgent),
            "checkout-api",
            summary: "Checkout symptom");
        await harness.AddEvidenceAsync(
            incidentId,
            EvidenceType.Database,
            "orders waits",
            nameof(DatabaseAgent),
            "orders-database",
            summary: "Orders waits");
        var agent = new AdaptiveRecordingAgent(
            nameof(DatabaseAgent),
            new AdditionalContextRequirement(
                EvidenceType.Database,
                "orders-database",
                null,
                null,
                "More wait evidence",
                "Continue focused retrieval."),
            alwaysRequest: true);
        var supervisor = harness.CreateSupervisor([agent], maximumRetrievalIterations: 2);

        var state = await supervisor.InvestigateAsync(
            incidentId,
            "Investigate checkout latency.",
            [new InvestigationAssignment(agent.Name, "Inspect database waits.")],
            default);

        Assert.Equal(2, agent.Contexts.Count);
        Assert.Equal(
            RetrievalAttemptStatus.IterationLimitReached,
            Assert.Single(state.RetrievalOutcomes).Status);
    }

    [Fact]
    public async Task Specialist_timeout_is_reported_without_unbounded_execution()
    {
        await using var harness = await TestHarness.CreateAsync();
        var agent = new SlowAgent(nameof(DatabaseAgent));
        var supervisor = harness.CreateSupervisor(
            [agent],
            retrievalTimeoutSeconds: 1);

        var state = await supervisor.InvestigateAsync(
            Guid.NewGuid(),
            "Investigate checkout latency.",
            [new InvestigationAssignment(agent.Name, "Inspect database waits.")],
            default);

        Assert.Equal(
            RetrievalAttemptStatus.TimedOut,
            Assert.Single(state.RetrievalOutcomes).Status);
    }

    private static EvidenceRecord Evidence(
        Guid incidentId,
        EvidenceType type,
        string component,
        string summary,
        DateTimeOffset observedFrom,
        DateTimeOffset observedTo)
    {
        return new EvidenceRecord
        {
            EvidenceId = Guid.NewGuid(),
            IncidentId = incidentId,
            SourceAgent = type == EvidenceType.Database
                ? nameof(DatabaseAgent)
                : nameof(ApplicationAgent),
            EvidenceType = type,
            Component = component,
            ObservedFrom = observedFrom,
            ObservedTo = observedTo,
            Summary = summary,
            DetailedContent = summary,
            CreatedAt = observedTo
        };
    }

    private sealed class AdaptiveRecordingAgent(
        string name,
        AdditionalContextRequirement requirement,
        bool alwaysRequest = false) : ISpecialistAgent
    {
        public string Name { get; } = name;

        public List<ContextBundle> Contexts { get; } = [];

        public Task<SpecialistFinding> InvestigateAsync(
            SpecialistRequest request,
            CancellationToken cancellationToken)
        {
            Contexts.Add(request.Context);
            var additionalContext = alwaysRequest || Contexts.Count == 1
                ? requirement
                : null;

            return Task.FromResult(new SpecialistFinding(
                Name,
                EvidenceType.Database,
                "orders-database",
                null,
                null,
                $"Database iteration {Contexts.Count}",
                "Structured database finding",
                "Connection waits contribute to latency.",
                0.75,
                additionalContext));
        }
    }

    private sealed class ScenarioAgent(
        string name,
        EvidenceType evidenceType,
        string component,
        string detailedContent) : ISpecialistAgent
    {
        public string Name { get; } = name;

        public List<ContextBundle> Contexts { get; } = [];

        public Task<SpecialistFinding> InvestigateAsync(
            SpecialistRequest request,
            CancellationToken cancellationToken)
        {
            Contexts.Add(request.Context);
            return Task.FromResult(new SpecialistFinding(
                Name,
                evidenceType,
                component,
                null,
                null,
                $"Compact finding from {Name}.",
                detailedContent,
                $"{component} contributes to checkout latency.",
                0.7));
        }
    }

    private sealed class SlowAgent(string name) : ISpecialistAgent
    {
        public string Name { get; } = name;

        public async Task<SpecialistFinding> InvestigateAsync(
            SpecialistRequest request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            throw new InvalidOperationException("The timeout guard should cancel this Agent.");
        }
    }

    private sealed class RecordingAgent(
        string name,
        EvidenceType evidenceType,
        string detailedContent) : ISpecialistAgent
    {
        public string Name { get; } = name;

        public int InvocationCount { get; private set; }

        public Task<SpecialistFinding> InvestigateAsync(
            SpecialistRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InvocationCount++;

            return Task.FromResult(new SpecialistFinding(
                Name,
                evidenceType,
                "checkout-api",
                null,
                null,
                "Compact finding summary.",
                detailedContent,
                "Deployment introduced a slower checkout path.",
                0.8));
        }
    }

    private sealed class TestHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly TestDbContextFactory _contextFactory;

        private TestHarness(SqliteConnection connection, TestDbContextFactory contextFactory)
        {
            _connection = connection;
            _contextFactory = contextFactory;
            Repository = new SqliteEvidenceRepository(contextFactory);
            var profiles = new AgentRetrievalProfileProvider();
            var specificationFactory = new RetrievalSpecificationFactory(profiles);
            ContextService = new EvidenceContextService(
                Repository,
                specificationFactory,
                new ExplainableEvidenceRanker(),
                new DeterministicEvidenceDeduplicator(),
                new DisabledSemanticEvidenceSearch());
        }

        public IEvidenceRepository Repository { get; }

        public IContextService ContextService { get; }

        public static async Task<TestHarness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<EvidenceDbContext>()
                .UseSqlite(connection)
                .Options;
            var contextFactory = new TestDbContextFactory(options);
            await using var database = await contextFactory.CreateDbContextAsync();
            await database.Database.EnsureCreatedAsync();

            return new TestHarness(connection, contextFactory);
        }

        public InvestigationSupervisor CreateSupervisor(
            IEnumerable<ISpecialistAgent> agents,
            int maximumInvocations = 8,
            int maximumRetrievalIterations = 2,
            int retrievalTimeoutSeconds = 10)
        {
            var profiles = new AgentRetrievalProfileProvider();
            return new InvestigationSupervisor(
                agents,
                ContextService,
                Repository,
                Options.Create(new InvestigationOptions
                {
                    MaximumSpecialistInvocations = maximumInvocations,
                    MaximumRetrievalIterationsPerTask = maximumRetrievalIterations,
                    RetrievalTimeoutSeconds = retrievalTimeoutSeconds
                }),
                new AdditionalContextValidator(profiles));
        }

        public Task AddEvidenceAsync(
            Guid incidentId,
            EvidenceType evidenceType,
            string detailedContent,
            string? sourceAgent = null,
            string component = "checkout-api",
            DateTimeOffset? observedFrom = null,
            DateTimeOffset? observedTo = null,
            string? summary = null,
            int importance = 3)
        {
            return Repository.AddAsync(
                new EvidenceRecord
                {
                    EvidenceId = Guid.NewGuid(),
                    IncidentId = incidentId,
                    SourceAgent = sourceAgent ?? SourceAgentFor(evidenceType),
                    EvidenceType = evidenceType,
                    Component = component,
                    ObservedFrom = observedFrom,
                    ObservedTo = observedTo,
                    Summary = summary ?? $"Seeded {evidenceType} evidence.",
                    DetailedContent = detailedContent,
                    Importance = importance,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                default);
        }

        private static string SourceAgentFor(EvidenceType evidenceType)
        {
            return evidenceType switch
            {
                EvidenceType.Log => nameof(LogInvestigationAgent),
                EvidenceType.Deployment => nameof(DeploymentAgent),
                EvidenceType.Database => nameof(DatabaseAgent),
                EvidenceType.Telemetry => nameof(InfrastructureTelemetryAgent),
                EvidenceType.Application => nameof(ApplicationAgent),
                _ => throw new ArgumentOutOfRangeException(nameof(evidenceType))
            };
        }

        public async ValueTask DisposeAsync()
        {
            await _connection.DisposeAsync();
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<EvidenceDbContext> options)
        : IDbContextFactory<EvidenceDbContext>
    {
        public EvidenceDbContext CreateDbContext()
        {
            return new EvidenceDbContext(options);
        }

        public Task<EvidenceDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateDbContext());
        }
    }
}
