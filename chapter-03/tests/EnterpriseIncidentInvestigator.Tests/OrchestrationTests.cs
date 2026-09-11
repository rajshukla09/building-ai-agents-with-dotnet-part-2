using EnterpriseIncidentInvestigator.Api.Agents;
using EnterpriseIncidentInvestigator.Api.Configuration;
using EnterpriseIncidentInvestigator.Api.Contracts;
using EnterpriseIncidentInvestigator.Api.Mcp;
using EnterpriseIncidentInvestigator.Api.Observability;
using EnterpriseIncidentInvestigator.Api.Orchestration;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EnterpriseIncidentInvestigator.Tests;

public sealed class OrchestrationTests
{
    [Fact]
    public void Catalog_registers_all_narrow_specialties()
    {
        var catalog = Agents().Select(x => x.Descriptor.Name).ToArray();
        Assert.Equal(["DeploymentAgent", "LogAnalysisAgent", "MetricsAgent", "DatabaseAgent"], catalog);
    }

    [Fact]
    public async Task Scripted_model_drives_multiple_agent_selection_and_context_updates()
    {
        var model = new ScriptedSupervisor(
            async agents =>
            {
                await agents.Single(x => x.Descriptor.Name == "DeploymentAgent")
                    .InvokeAsync("Find releases near onset.", "Timing suggests a release correlation.", default);
                await agents.Single(x => x.Descriptor.Name == "LogAnalysisAgent")
                    .InvokeAsync("Correlate errors with that release.",
                                 "Release evidence leaves the exception unknown.", default);
            });
        var response = await Create(model).InvestigateAsync("Checkout failed after deployment.");
        Assert.Equal(["DeploymentAgent", "LogAnalysisAgent"], response.AgentInvocations.Select(x => x.Agent).ToArray());
        Assert.Equal(2, response.Findings.Count);
        Assert.All(response.AgentInvocations, x => Assert.True(x.Succeeded));
        Assert.Contains("sufficient", response.TerminationReason);
    }

    [Fact]
    public async Task Meaningful_repeated_agent_invocation_is_supported()
    {
        var model = new ScriptedSupervisor(
            async agents =>
            {
                var logs = agents.Single(x => x.Descriptor.Name == "LogAnalysisAgent");
                await logs.InvokeAsync("Find dominant exceptions.", "Unknown failure requires an initial log pattern.",
                                       default);
                await logs.InvokeAsync("Search specifically for version v4.8.",
                                       "New version evidence focuses a distinct second log search.", default);
            });
        var response = await Create(model).InvestigateAsync("Intermittent checkout failures.");
        Assert.Equal(2, response.AgentInvocations.Count(x => x.Agent == "LogAnalysisAgent"));
        Assert.Equal(new[] { 1, 2 }, response.AgentInvocations.Select(x => x.Sequence));
    }

    [Theory]
    [InlineData("Checkout failures increased after this morning's deployment.", "DeploymentAgent")]
    [InlineData("Order API latency increased and SQL timeout errors are appearing.", "DatabaseAgent")]
    [InlineData("Users report intermittent checkout failures.", "LogAnalysisAgent")]
    public async Task Validation_scenarios_allow_supervisor_selected_specialists(string incident, string selectedAgent)
    {
        var model = new ScriptedSupervisor(
            agents => agents.Single(x => x.Descriptor.Name == selectedAgent)
                          .InvokeAsync("Investigate the highest-value uncertainty.",
                                       "The incident evidence makes this specialty useful.", default));
        var response = await Create(model).InvestigateAsync(incident);
        Assert.Equal(selectedAgent, Assert.Single(response.AgentInvocations).Agent);
        Assert.True(response.AgentInvocations[0].Succeeded);
    }

    [Fact]
    public async Task Maximum_agent_limit_stops_delegation_and_still_synthesizes()
    {
        var store = new RecordingStore();
        var model = new ScriptedSupervisor(async agents =>
                                           {
                                               await agents[0].InvokeAsync("one", "first uncertainty", default);
                                               await agents[1].InvokeAsync("two", "remaining uncertainty", default);
                                           });
        var response = await Create(model, maxAgents: 1, store: store).InvestigateAsync("Investigate.");
        Assert.True(response.InvestigationLimitReached);
        Assert.Single(response.AgentInvocations);
        Assert.Equal("Evidence-based conclusion", response.RootCause);
        Assert.Contains("Maximum", response.TerminationReason);
        Assert.Contains(store.Items,
                        x => x.EventType == InvestigationEventTypes.LimitReached && x.Status == "LimitReached");
    }

    [Fact]
    public async Task Failure_is_observable_and_best_available_conclusion_is_returned()
    {
        var store = new RecordingStore();
        var failing = new FakeAgent("DatabaseAgent", fail: true);
        var model = new ScriptedSupervisor(
            agents => agents.Single().InvokeAsync("Check health.", "SQL uncertainty remains.", default));
        var response = await Create(model, agents: [failing], store: store).InvestigateAsync("SQL timeouts.");
        Assert.False(Assert.Single(response.AgentInvocations).Succeeded);
        Assert.Contains("Fatal failure", response.TerminationReason);
        Assert.Equal("Evidence-based conclusion", response.RootCause);
        var failure = Assert.Single(store.Items, x => x.EventType == InvestigationEventTypes.AgentFailed);
        Assert.Equal("DatabaseAgent", failure.Agent);
        Assert.Equal("demo failure", failure.ErrorSummary);
    }

    [Fact]
    public async Task Every_mcp_server_exposes_complete_catalog_and_every_tool_path_executes()
    {
        await using var client = McpClient();
        var expected =
            new Dictionary<string,
                           string[]> { ["deployment"] =
                                           ["get_recent_deployments", "get_release_changes", "get_deployment_status"],
                                       ["logs"] = ["search_logs", "find_error_patterns", "get_trace"],
                                       ["metrics"] = ["get_service_metrics", "get_error_rate", "compare_metrics"],
                                       ["database"] =
                                           ["get_db_health", "get_slow_queries", "get_connection_failures"] };
        Assert.Equal(expected.Keys, client.Servers.Select(x => x.Name));
        foreach (var (server, names) in expected)
        {
            var catalog = client.DiscoverTools(server);
            Assert.Equal(names.Order(), catalog.Select(x => x.Name).Order());
            foreach (var tool in catalog)
                Assert.False(string.IsNullOrWhiteSpace(
                    await client.InvokeAsync(new(server, tool.Name, "validation scenario"), default)));
        }
    }

    [Fact]
    public async Task Every_real_specialized_agent_maps_to_one_discoverable_mcp_catalog()
    {
        await using var client = McpClient();
        var limits = Options.Create(new InvestigationOptions());
        var azure = Options.Create(new AzureOpenAIOptions { Endpoint = "https://example.openai.azure.com",
                                                            ApiKey = "test", DeploymentName = "test" });
        ISpecializedAgent[] agents = [
            new DeploymentAgent(client, limits, azure), new LogAnalysisAgent(client, limits, azure),
            new MetricsAgent(client, limits, azure), new DatabaseAgent(client, limits, azure)
        ];
        Assert.Equal(["DeploymentAgent", "LogAnalysisAgent", "MetricsAgent", "DatabaseAgent"],
                     agents.Select(x => x.Descriptor.Name));
        Assert.All(agents, agent =>
                           {
                               Assert.NotNull(agent.Descriptor.McpServer);
                               Assert.Equal(3, agent.Descriptor.McpTools!.Count);
                               Assert.Equal(agent.Descriptor.McpTools,
                                            client.DiscoverTools(agent.Descriptor.McpServer!).Select(x => x.Name));
                           });
    }

    [Fact]
    public async Task Unknown_mcp_tool_is_rejected_after_real_server_discovery()
    {
        await using var client = McpClient();
        _ = client.DiscoverTools("logs");
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.InvokeAsync(new("logs", "not_a_real_tool", "incident"), default));
        Assert.Contains("was not discovered", exception.Message);
    }

    [Fact]
    public async Task Multiple_specialist_catalogs_invoke_multiple_tools_over_stdio()
    {
        await using var client = McpClient();
        var deploymentTool = client.DiscoverTools("deployment").Single(x => x.Name == "get_recent_deployments");
        var logTool = client.DiscoverTools("logs").Single(x => x.Name == "search_logs");

        var deployment = await client.InvokeAsync(new("deployment", deploymentTool.Name, "checkout"), default);
        var logs = await client.InvokeAsync(new("logs", logTool.Name, "checkout v4.8"), default);

        Assert.Contains("v4.8", deployment);
        Assert.Contains("NullReferenceException", logs);
    }

    [Fact]
    public void Supervisor_has_no_keyword_router_or_fixed_agent_order()
    {
        var methods = typeof(IncidentInvestigator)
                          .GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static |
                                      System.Reflection.BindingFlags.Instance);
        Assert.DoesNotContain(methods, x => x.Name is "Route" or "Plan" or "Contains");
    }

    [Fact]
    public async Task Correlated_events_capture_decision_agent_tool_and_final_result_in_order()
    {
        var store = new RecordingStore();
        var model = new ScriptedSupervisor(
            agents => agents[0].InvokeAsync("Inspect release timing.", "Release timing is unknown.", default));
        var response = await Create(model, store: store).InvestigateAsync("Checkout failures.");
        Assert.Equal(response.RunId, store.RunId);
        Assert.Equal(
            [
                InvestigationEventTypes.Started, InvestigationEventTypes.SupervisorDecision,
                InvestigationEventTypes.AgentStarted, InvestigationEventTypes.ToolCompleted,
                InvestigationEventTypes.AgentCompleted, InvestigationEventTypes.RootCauseGenerated,
                InvestigationEventTypes.Completed
            ],
            store.Items.Select(x => x.EventType));
        var tool = Assert.Single(store.Items, x => x.EventType == InvestigationEventTypes.ToolCompleted);
        Assert.Equal("DeploymentAgent", tool.Agent);
        Assert.Equal(1, tool.AgentInvocationSequence);
        Assert.Equal("demo_mcp_tool", tool.Tool);
        Assert.Equal("Release timing is unknown.",
                     Assert.Single(store.Items, x => x.EventType == InvestigationEventTypes.SupervisorDecision).Reason);
        Assert.NotNull(store.Result);
    }

    [Fact]
    public async Task Failed_mcp_event_remains_attached_to_its_agent_invocation()
    {
        var store = new RecordingStore();
        var agent = new FakeAgent("MetricsAgent", toolFail: true);
        var model = new ScriptedSupervisor(
            agents => agents.Single().InvokeAsync("Measure impact.", "Impact is unknown.", default));
        await Create(model, agents: [agent], store: store).InvestigateAsync("Latency increased.");
        var tool = Assert.Single(store.Items, x => x.EventType == InvestigationEventTypes.ToolCompleted);
        Assert.Equal("Failed", tool.Status);
        Assert.Equal("MetricsAgent", tool.Agent);
        Assert.Equal(1, tool.AgentInvocationSequence);
        Assert.Equal("tool timeout", tool.ErrorSummary);
    }

    [Fact]
    public async Task Sqlite_store_survives_context_recreation_and_reconstructs_run()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var dbOptions = new DbContextOptionsBuilder<InvestigationDbContext>().UseSqlite(connection).Options;
        var id = Guid.NewGuid();
        await using (var db = new InvestigationDbContext(dbOptions))
        {
            await db.Database.EnsureCreatedAsync();
            var store = new SqliteInvestigationEventStore(db, new NoopPublisher());
            await store.CreateAsync(id, "Persist me", default);
            await store.AppendAsync(id,
                                    new(InvestigationEventTypes.SupervisorDecision, "Selected", "MetricsAgent", 1,
                                        Reason: "Measure impact."),
                                    default);
            var result = new InvestigationResponse("summary", "cause", .7, "action", [], [], false, "done", id);
            await store.CompleteAsync(id, "Completed", result, default);
        }
        await using (var db = new InvestigationDbContext(dbOptions))
        {
            var restored = await new SqliteInvestigationEventStore(db, new NoopPublisher()).GetAsync(id, 8, 5, default);
            Assert.NotNull(restored);
            Assert.Equal("Completed", restored.Run.Status);
            Assert.Equal("MetricsAgent", restored.Events[1].Agent);
            Assert.Equal("cause", restored.Result!.RootCause);
        }
    }

    private static IIncidentInvestigator Create(ISupervisorModel model, int maxAgents = 8,
                                                IEnumerable<ISpecializedAgent>? agents = null,
                                                IInvestigationEventStore? store = null) =>
        new IncidentInvestigator(agents ?? Agents(), model, new FakeRootCause(), store ?? new RecordingStore(),
                                 Options.Create(new InvestigationOptions { MaxAgentInvocations = maxAgents,
                                                                           MaxToolCallsPerAgent = 5 }),
                                 NullLogger<IncidentInvestigator>.Instance);

    private static ISpecializedAgent[] Agents() => [new FakeAgent("DeploymentAgent"), new FakeAgent("LogAnalysisAgent"),
                                                    new FakeAgent("MetricsAgent"), new FakeAgent("DatabaseAgent")];

    private static IncidentMcpClient McpClient() => new(
        NullLoggerFactory.Instance, NullLogger<IncidentMcpClient>.Instance);

    private sealed class ScriptedSupervisor(Func<IReadOnlyList<AgentDelegate>, Task> script) : ISupervisorModel
    {
        public async Task<string> RunAsync(string incident, string context, IReadOnlyList<AgentDelegate> agents,
                                           CancellationToken cancellationToken)
        {
            await script(agents);
            return "sufficient";
        }
    }

    private sealed class FakeAgent(string name, bool fail = false, bool toolFail = false) : ISpecializedAgent
    {
        public AgentDescriptor Descriptor => new(name, $"Narrow responsibility for {name}.");

        public Task<AgentFinding> InvestigateAsync(IncidentInvestigationContext context, string assignment,
                                                   CancellationToken cancellationToken) =>
            fail ? throw new InvalidOperationException("demo failure")
                 : Task.FromResult(new AgentFinding(
                       name, $"{assignment} Found evidence after {context.Findings.Count} prior finding(s).",
                       [new(name, "evidence")], .8, "reassess",
                       [new(1, "demo_mcp_tool", 1, !toolFail, "demo", toolFail ? "tool timeout" : null)]));
    }

    private sealed class FakeRootCause : IRootCauseAgent
    {
        public Task<RootCauseConclusion> SynthesizeAsync(IncidentInvestigationContext context, bool limitReached,
                                                         CancellationToken cancellationToken) =>
            Task.FromResult(new RootCauseConclusion("Final synthesis", "Evidence-based conclusion", .8,
                                                    "Mitigate and verify."));
    }

    private sealed class NoopPublisher : IInvestigationUpdatePublisher

    {
        public Task PublishAsync(Guid runId, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class RecordingStore : IInvestigationEventStore
    {
        public Guid RunId { get; private set; }
        public List<NewInvestigationEvent> Items { get; } = [];
        public InvestigationResponse? Result { get; private set; }

        public Task CreateAsync(Guid runId, string incident, CancellationToken ct)
        {
            RunId = runId;
            Items.Add(new(InvestigationEventTypes.Started, "Running"));
            return Task.CompletedTask;
        }

        public Task AppendAsync(Guid runId, NewInvestigationEvent item, CancellationToken ct)
        {
            Items.Add(item);
            return Task.CompletedTask;
        }

        public Task CompleteAsync(Guid runId, string status, InvestigationResponse response, CancellationToken ct)
        {
            Result = response;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<InvestigationRunSummary>>
        ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<InvestigationRunSummary>>([]);

        public Task<InvestigationRunView?> GetAsync(Guid runId, int maxAgents, int maxTools, CancellationToken ct) =>
            Task.FromResult<InvestigationRunView?>(null);
    }
}
