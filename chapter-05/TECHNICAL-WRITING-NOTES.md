# Chapter 5 Technical Writing Notes — Multi-Agent Orchestration Patterns

## Chapter Purpose
Compares native Sequential, Concurrent, Handoff, and Group Chat patterns using the same business request. Readers learn that topology choice changes responsibility, timing, and event shape.

## Application / Use Case
Security, Quality, Architecture, and Release Agents review a software release. The user can run one request through each pattern, inspect events/history, and compare persisted metrics.

## Architecture
`Blazor → ReviewsController → ReviewExecutionService → selected IReleaseReview → AgentWorkflowBuilder pattern → AIAgents → ReviewResponse → SQLite`

## End-to-End Execution Flow
1. UI selects a pattern and posts identical release text.
2. `ReviewExecutionService` resolves the allow-listed implementation.
3. Pattern class builds/runs a native MAF Agent Workflow.
4. Sequential chains Agents; Concurrent fans out reviewers then runs Release decision; Handoff lets Agents transfer control; Group Chat uses manager-mediated participation.
5. `WorkflowResultReader` and `ReadableEventProjector` convert native events to safe results.
6. Run, Agent executions, and events persist to SQLite; history/compare read them.

## Important Code
- `ReleaseReviewAgentFactory` builds configured `AIAgent`s; four Agent wrapper classes own roles.
- `SequentialReview`, `ConcurrentReview`, `HandoffReview`, and `GroupChatReview` own their native topology.
- `ReviewExecutionService` owns pattern resolution, execution lifecycle, and persistence.
- `ReadableEventProjector`/`WorkflowResultReader` isolate version-sensitive runtime event interpretation.
- repositories and `RunComparisonService` own persisted views and deterministic metrics.

## Important MAF APIs
`AgentWorkflowBuilder.BuildSequential`, `BuildConcurrent`, `CreateHandoffBuilderWith`, and Group Chat builder APIs construct native workflows; `InProcessExecution.RunAsync` hosts them; `AIAgent` participants do model work.

## Data and State Flow
The same release text enters each topology. Native events and Agent outputs become `ReviewEvent`, decision, execution timing, and EF entities. Compare reads stored metrics rather than rerunning Agents.

## Agent Decision Points
Agents produce findings/decisions; Handoff and Group Chat include model-driven participation/control choices. C# deterministically selects the requested topology and computes comparison metrics.

## Guardrails and Validation
Only four known pattern names are resolvable; cancellation propagates; failures persist safe run state; event projection avoids raw prompts/secrets.

## Persistence and Recovery
SQLite preserves completed/failed runs for history and comparison. It does not resume an interrupted in-process workflow.

## Observability
Native runtime event types, Executor/Agent labels, text projections, durations, fan-out/fan-in timings, decisions, and errors are displayed.

## UI Flow
Pattern cards explain/diagram topology; Run executes it; result panels map to projected native events; History reopens persisted runs; Compare contrasts two to four runs.

## Demo / Test Scenario
Run the same release through Sequential and Concurrent. Observe ordered versus overlapping reviews and compare duration/Agent sequence before trying Handoff and Group Chat.

## Failure / Edge Cases
Unknown pattern, model/provider failure, missing final output, cancellation, and persistence failure produce a failed run rather than fabricated success.

## Key Teaching Points
- Pattern selection is an architectural decision, not a cosmetic graph change.
- Native event projection should remain separate from business naming.

## Common Misunderstandings
Concurrent Agents are still in-process MAF participants, not A2A services. Magentic is not implemented on this page; Chapter 4 owns it.

## What This Chapter Does Not Cover
MCP, durable workflows, OpenTelemetry export, or production authorization.

## Connection to Other Chapters
Contrasts with open-ended Chapter 4. Chapter 7 applies native telemetry to an explicit workflow topology.
