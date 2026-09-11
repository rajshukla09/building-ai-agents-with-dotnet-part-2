# Verified Writing Context — Part 2, Chapter 3

## Dynamic Multi-Agent Orchestration

Verified against `chapter-03` and its sixteen passing tests.

## What this chapter should teach

Teach dynamic delegation through a supervisor `AIAgent` whose functions invoke specialist Agents. The supervisor chooses who works, each specialist chooses which MCP capability to use, and application code owns shared context, budgets, events, persistence, and final synthesis. This is custom application orchestration using MAF Agents and model tool calling—not native Magentic orchestration.

## What it adds over Chapter 2

Chapter 2 exposed MCP tools directly to one Agent. Chapter 3 adds four specialists, supervisor-selected and repeated delegation, shared investigation context, a separate root-cause Agent, SQLite history, SignalR update notifications, and a Blazor observer UI. Reference Chapter 2 for MCP transport and discovery mechanics.

## Actual end-to-end flow

```text
POST /api/incidents/investigate
  → IncidentInvestigator creates persisted run
  → wrap each specialist as AgentDelegate
  → MafSupervisorModel converts delegates to AIFunctions
  → SupervisorAgent.RunAsync
  → model selects a specialist and supplies assignment/reason
  → reserve invocation sequence and persist decision/start events
  → specialist discovers its dedicated MCP catalog
  → specialist AIAgent selects MCP tools over stdio
  → AgentFinding updates shared context and persisted events
  → supervisor may select another or repeated specialist
  → RootCauseAgent synthesizes accumulated evidence
  → persist final response
  → SignalR sends RunUpdated; UI refetches persisted state
```

Specialists are `DeploymentAgent`, `LogAnalysisAgent`, `MetricsAgent`, and `DatabaseAgent`, each mapped to its own MCP server.

## Important code and APIs

- `IncidentInvestigator.cs`: delegate construction, budgets, context updates, failures, synthesis.
- `MafSupervisorModel.cs`: specialists as `AIFunction`s and supervisor instructions.
- `SpecializedAgents.cs`: specialist `AIAgent` creation and MCP tool binding.
- `IncidentInvestigationContext.cs`: findings and invocation history.
- `IncidentMcp.cs`: four real stdio MCP client/process boundaries.
- `RootCauseAgent.cs`: final model synthesis and labeled-line parser.
- `InvestigationPersistence.cs`: SQLite event store, SignalR hub, and update publisher.

Important APIs include `AIFunctionFactory.Create`, `AsAIAgent`, `AIAgent.RunAsync`, official MCP stdio APIs, EF Core SQLite, and `IHubContext`.

## Model-driven versus deterministic

Model-driven: supervisor selection, assignment and public reason, repeated delegation, specialist tool choice, specialist findings, and final synthesis.

Deterministic: specialist catalog and MCP mapping, invocation limits, context mutation, event correlation, persistence, SignalR notification, failure records, and demo MCP data.

## Persistence and runtime

Runs, ordered events, and final responses are persisted in SQLite using `EnsureCreated`. Completed history survives `DbContext` recreation and process restart. Active supervisor/Agent execution is not checkpointed and cannot resume. SignalR sends only `RunUpdated`; the UI reads full state from the API. MCP connections and catalogs are process-local.

## Tests: exactly what they prove

Tests prove the four-specialist catalog; scripted multiple and repeated selections; deterministic agent-limit behavior; observable failure plus best-available synthesis; real discovery and execution across all four MCP servers; specialist/catalog mapping; unknown-tool rejection; multiple stdio catalogs; absence of named router helpers; correlated event ordering; failed-tool correlation; and SQLite reconstruction.

They do not prove live Azure OpenAI routing, live specialist tool choice, browser SignalR delivery, restart continuation, or root-cause parsing quality under arbitrary model output.

## Limitations

- Not a native MAF workflow builder or Magentic workflow.
- Supervisor delegation happens through tool calls inside one `AIAgent.RunAsync`.
- A specialist failure stops normal supervisor execution; best-available synthesis follows.
- Assignments, arguments, results, and failure text can be logged or persisted.
- Root-cause output uses prose labels rather than structured output.
- No durable queue, active-run recovery, authentication, authorization, or configured OTLP export.
- Tool budgets are recreated for each specialist invocation.

## Claims to avoid

Do not claim native Magentic orchestration, MAF-owned shared context, restart resumption, live-model test coverage, full payload delivery through SignalR, configured OTLP export, continued planning after specialist failure, or captured chain-of-thought.

## Recommended sections

1. From one tool-using Agent to specialists
2. Narrow specialist responsibilities
3. Specialists as supervisor functions
4. Dynamic delegation without keyword routing
5. Shared investigation context
6. Specialist-selected MCP tools
7. Agent and tool budgets
8. Repeated delegation
9. Root-cause synthesis and failure behavior
10. Persisting the execution narrative
11. SignalR and the observer UI
12. Tests and non-durable boundaries

## Reference instead of repeating

Reference Chapter 2 for MCP process setup, discovery, and tool adaptation. Reference Chapter 1 for authority boundaries. Use this chapter as the baseline custom-supervisor design contrasted with native Magentic in Chapter 4.
