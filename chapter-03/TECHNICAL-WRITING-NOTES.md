# Chapter 3 Technical Writing Notes — Dynamic Multi-Agent Orchestration

## Chapter Purpose
Adds model-driven Supervisor delegation to Specialist Agents while preserving real MCP capability boundaries. Readers learn to distinguish selecting an Agent from selecting an MCP Tool.

## Application / Use Case
An incident investigator analyzes a checkout failure using Deployment, Log Analysis, Metrics, and Database Specialist Agents, then a tool-free Root Cause Agent synthesizes findings.

## Architecture
`Blazor → API → InvestigationOrchestrator → Supervisor Agent → Specialist Agent → IncidentMcpClient → stdio MCP Server → sample incident tool`
`accumulated findings → RootCauseAgent → persisted result`

## End-to-End Execution Flow
1. UI generates a run ID, subscribes through SignalR, and posts the incident.
2. The API persists a queued run and invokes orchestration.
3. Supervisor receives the incident, available Agent catalog, and current findings; it selects the next Specialist Agent or stops.
4. The selected `McpSpecializedAgent` discovers only its domain MCP catalog from a child stdio server.
5. The model selects MCP Tools; client validation/budgets bound calls.
6. A typed `AgentFinding` returns and is added to Supervisor context.
7. Delegation may repeat until sufficient evidence or the Agent budget ends.
8. `RootCauseAgent` synthesizes the final structured diagnosis without tools.
9. SQLite events/results drive live and historical UI views.

## Important Code
- `InvestigationOrchestrator` owns the bounded delegation loop and final synthesis.
- `SupervisorAgent` produces structured selection; `ISupervisorModel` permits scripted tests.
- `McpSpecializedAgent` and domain Agent registrations scope instructions/tools.
- `IncidentMcpClient` owns official MCP client lifecycle, stdio discovery, call validation, and tool budgets.
- MCP server tool classes expose Deployment/Logs/Metrics/Database sample capabilities.
- persistence repositories and SignalR publish safe projections; the UI components render paths, Agent cards, and nested calls.

## Important MAF APIs
Each Supervisor, Specialist, and Root Cause component is an `AIAgent` created with `AsAIAgent`; structured `RunAsync<T>` returns selections/findings. MCP uses official client/server/stdio APIs and `[McpServerTool]` declarations.

## Data and State Flow
Incident plus accumulated typed findings enter each Supervisor turn. Agent selection creates an invocation-scoped MCP catalog. Tool JSON results become `AgentFinding`; safe event records and final diagnosis persist in SQLite.

## Agent Decision Points
The Supervisor model decides who works next; a Specialist model decides which MCP capability to use; Root Cause model synthesizes. C# enforces catalogs, budgets, stop states, persistence, and error policy.

## Guardrails and Validation
Allow-listed Agent names, `MaxAgentInvocations`, per-Agent MCP Tool budgets, read-only catalogs, structured findings, sanitized errors, cancellation, and final synthesis over available evidence constrain execution.

## Persistence and Recovery
SQLite stores run/event/result projections and supports history after restart. It does not checkpoint and resume an in-flight model delegation loop.

## Observability
SignalR updates, hierarchical events, durations, Supervisor rationale, Agent paths, MCP discovery/calls, failures, and ActivitySource spans are available. Raw hidden reasoning/prompts are not persisted.

## UI Flow
The request card starts a run; route path shows actual Supervisor selections; each Agent card expands its MCP Client/server/tool calls; timeline and root-cause panels map to persisted backend events/result.

## Demo / Test Scenario
Submit “Checkout failures increased after this morning's deployment.” Expect deployment/log/metrics evidence, potentially repeated delegation, and a payment-token mapping root cause from sample data.

## Failure / Edge Cases
MCP child process unavailable, failed Specialist, tool/Agent limit reached, cancellation, and incomplete evidence still produce explicit termination and safe synthesis where possible.

## Key Teaching Points
- Supervisor chooses who; Specialist chooses what tool; MCP carries the capability call.
- Multi-agent orchestration is MAF application coordination, not MCP.

## Common Misunderstandings
These Agents run in one application orchestration; they are not A2A peers. MCP transport is real, while incident data is deterministic sample data.

## What This Chapter Does Not Cover
Magentic planning/replanning, distributed Agent hosting, durable continuation, or production observability exporters.

## Connection to Other Chapters
Builds on Chapter 2. Chapter 4 delegates open-ended planning to native Magentic; Chapter 8 moves Agent communication across A2A.
