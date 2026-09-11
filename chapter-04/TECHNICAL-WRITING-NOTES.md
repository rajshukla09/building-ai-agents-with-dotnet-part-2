# Chapter 4 Technical Writing Notes — Magentic Orchestration

## Chapter Purpose
Introduces native MAF Magentic planning, delegation, progress evaluation, and replanning for an open-ended objective. Readers learn when a manager-led plan is more appropriate than Chapter 3's next-Agent Supervisor loop.

## Application / Use Case
An enterprise architecture assessment coordinates Architecture, Security, Cost, Operations, Migration, and Research Specialist Agents, each backed by a dedicated MCP stdio server.

## Architecture
`Blazor → API → AssessmentService → MagenticWorkflowBuilder(manager + six Agents) → Specialist Agent → MCP Client → stdio MCP Server → sample evidence → manager plan/result`

## End-to-End Execution Flow
1. UI posts a broad assessment objective.
2. `AssessmentService` creates/persists a run and calls the Magentic orchestrator.
3. `MagenticWorkflowBuilder` receives manager and participant descriptions plus round/stall/reset limits.
4. MAF's manager creates a plan, delegates, evaluates progress, may revise/reset, and declares completion.
5. Each participant discovers and selects tools only from its MCP server process.
6. Runtime envelopes and intentional manager audit labels are projected to safe plan/delegation/finding events.
7. Recommendation, metrics, configuration, and events persist to SQLite.

## Important Code
- `MagenticAssessmentOrchestrator` constructs/runs the native Magentic Workflow and projects events.
- `AssessmentAgentFactory` creates manager and focused participant `AIAgent` instances.
- `McpToolProvider`/MCP client code starts per-domain stdio servers and records calls.
- `AssessmentService` owns run lifecycle and persistence boundary.
- `AssessmentRunRepository` and EF entities/migrations store audit projections.
- Blazor `Home.razor` renders run, plan revisions, delegations, findings, history, and comparison.

## Important MAF APIs
`MagenticWorkflowBuilder`, manager/participant `AIAgent`s, `WithMaxRounds`, `WithMaxStalls`, `WithMaxResets`, and `InProcessExecution.RunAsync` provide native orchestration. Official MCP APIs provide stdio clients/servers and tools.

## Data and State Flow
The objective enters the manager. Manager messages create audit-label projections; participant outputs and MCP results feed Magentic context. `AssessmentView` persists objective, plans, delegations, findings, counters, completion reason, and recommendation—not chain-of-thought.

## Agent Decision Points
The Magentic manager decides plan, delegation, progress, replanning, and completion; participant Agents choose MCP Tools and produce findings. C# enforces participants, limits, MCP catalogs, projection rules, persistence, and comparison math.

## Guardrails and Validation
Round/stall/reset, Agent-invocation and per-Agent tool-call limits, read-only tools, typed/safe projections, cancellation, and reduced-confidence wording at limits bound open-ended execution.

## Persistence and Recovery
SQLite stores completed/failed audit views and supports history/comparison after restart. The in-process Magentic run itself is not resumed from a checkpoint.

## Observability
Timeline events, plan versions, delegations, Agent/MCP counts, failures, limits, durations, and run comparison are visible in the UI and logs.

## UI Flow
Run Assessment starts Magentic; current-plan/revision panels reflect manager audit events; Agent/MCP timelines show execution; history reloads persisted runs; compare uses deterministic stored metrics.

## Demo / Test Scenario
Assess modernization of an order-processing system. Observe an initial plan, several Specialist Agents using focused MCP Tools, possible revision, and a final recommendation.

## Failure / Edge Cases
MCP failure, participant failure, stalled plan, round/tool limits, cancellation, or incomplete evidence produce explicit completion/limit/failure state.

## Key Teaching Points
- Magentic owns planning/replanning rather than following a predetermined graph.
- Persist only observable audit artifacts, not hidden reasoning.

## Common Misunderstandings
Magentic is MAF orchestration, not an MCP feature. Real MCP transport fronts deterministic sample enterprise evidence.

## What This Chapter Does Not Cover
Durable Magentic continuation, production enterprise systems, A2A, or autonomous deployment changes.

## Connection to Other Chapters
Extends Chapter 3's dynamic delegation. Chapter 5 compares more constrained native multi-agent patterns.
