# Chapter 4 — Magentic Orchestration

This runnable .NET 9 sample assesses an order-processing architecture with the **native Microsoft Agent Framework Magentic orchestration** in `Microsoft.Agents.AI.Workflows` 1.15.0. Configure Azure OpenAI in `src/EnterpriseArchitectureAssessment.Api/appsettings.json`, run the API, then run the Blazor WebAssembly viewer.

Assessment runs are stored in SQLite through EF Core using the configurable `ConnectionStrings:Assessments` connection string. The viewer's **Run History** tab reopens persisted runs after restart, while **Compare Runs** deterministically compares duration, agents, MCP activity, plans, outcomes, and the captured model/guardrail configuration. Apply schema changes with EF Core migrations; the API applies pending migrations at startup.

## Chapter 3 versus Chapter 4

**Chapter 3 — dynamic routing:** “Which agent should work next?” Its supervisor selected one next specialist after each finding.

**Chapter 4 — Magentic orchestration:** “What work is required? Who should do it? What changed? What remains? Should the plan change? Are we finished?” `MagenticWorkflowBuilder` gives the broad objective and participant descriptions to a manager agent. MAF owns plan formation, delegation, evaluation, repeated participation, revision, and completion—not an application `while` loop or keyword router.

## Native setup

`MagenticAssessmentOrchestrator` constructs six `AIAgent` participants and the manager, then uses:

```csharp
Workflow workflow = new MagenticWorkflowBuilder(managerAgent)
    .AddParticipants(participants)
    .RequirePlanSignoff(false)
    .WithMaxRounds(12)
    .WithMaxStalls(3)
    .WithMaxResets(2)
    .Build();
await InProcessExecution.RunAsync(workflow, objective, cancellationToken: ct);
```

The installed repository baseline is MAF **1.15.0**. The exact native types are `Microsoft.Agents.AI.Workflows.Specialized.MagenticWorkflowBuilder`, `Workflow`, `InProcessExecution.RunAsync`, `WorkflowOutputEvent`, `AIAgent`, and `ChatClientAgentOptions`. `WithMaxRounds`, `WithMaxStalls`, and `WithMaxResets` configure native Magentic termination guardrails. `MaxAgentInvocations` and `MaxToolCallsPerAgent` are host policy budgets documented for integration instrumentation. In 1.15.0, the public run result exposes workflow event envelopes and output payloads, but does **not** expose Magentic's internal ledger, plan, stall counter, replanning decision, or completion reason as public typed models. The sample therefore maps concrete invocation/completion envelopes and manager-authored labeled audit summaries only when they occur in runtime payloads. It never reconstructs absent state. Private reasoning is never requested or stored.

## Team and MCP capabilities

| Participant | MCP server and tools |
|---|---|
| ArchitectureAgent | architecture: `get_current_architecture`, `get_service_dependencies`, `get_traffic_profile`, `search_architecture_documents` |
| SecurityAgent | security: `get_security_standards`, `get_authentication_model`, `get_data_classification`, `search_security_findings` |
| CostAgent | cost: `get_current_hosting_cost`, `estimate_target_architecture_cost`, `get_team_operating_cost` |
| OperationsAgent | operations: `get_incident_history`, `get_deployment_frequency`, `get_operational_metrics`, `get_sla_history` |
| MigrationAgent | migration: `get_dependency_map`, `get_release_constraints`, `get_migration_history`, `estimate_migration_risk` |
| ResearchAgent | research: `search_internal_docs`, `get_previous_architecture_decisions`, `get_reference_projects` |

Each MAF agent receives only focused instructions and the functions discovered from its dedicated MCP server. The API starts each server as a separate process and communicates through MCP's stdio transport; the deterministic tool bodies stand in for enterprise systems while preserving the production hierarchy: manager → specialist → MCP client → MCP transport → MCP server → tool.

## Behavior and safety

The initial plan emerges from the objective and participant descriptions. Cost language can make cost/operations useful early, while a payment crisis can narrow the scope; no scenario-to-route mapping exists. New low scaling pressure, high full-fleet cost, or weak operational maturity gives the manager evidence to revise toward payment extraction and a modular monolith. The prompt tells it to reconcile positions as trade-offs instead of voting or averaging. Agents may be invoked repeatedly after new evidence.

Limits default to 12 manager turns, 10 agent invocations, and 5 tool calls per agent. MAF can finish early. A limit-conditioned answer must identify reduced confidence. `AgentStarted` and `AgentCompleted` are emitted from native invocation/completion envelopes. `PlanCreated`, `TaskDelegated`, `ProgressEvaluated`, `PlanRevised`, `ConflictDetected`, and `CompletionDeclared` are emitted only when the corresponding manager-authored audit label is present. MCP calls are recorded at the real adapter boundary. Empty, unexposed artifacts are omitted from the viewer rather than represented by placeholder sections or events.

## API and scenarios

* `POST /api/assessments` with `{ "objective": "..." }`
* `GET /api/assessments`
* `GET /api/assessments/{id}`
* `GET /api/assessments/{id}/events`

The viewer offers full-migration, 20%-cost-reduction, and payment-scalability prompts. Swagger remains usable independently at `/swagger`.

```bash
dotnet run --project src/EnterpriseArchitectureAssessment.Api
dotnet run --project src/EnterpriseArchitectureAssessment.Web
dotnet test EnterpriseArchitectureAssessment.sln
```
