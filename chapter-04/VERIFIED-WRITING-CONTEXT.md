# Verified Writing Context — Part 2, Chapter 4

## Magentic Orchestration

Verified against `chapter-04` and its sixteen passing tests. Current package references use MAF `1.17.0`; older repository prose mentioning `1.15.0` is stale.

## What this chapter should teach

Teach the move from custom supervisor delegation to native MAF Magentic orchestration. A manager Agent receives an objective and participant descriptions, then owns planning, delegation, progress evaluation, replanning, and completion within the native workflow runtime.

## What it adds over Chapter 3

Chapter 3 models specialists as supervisor functions. Chapter 4 introduces `MagenticWorkflowBuilder`, native manager guardrails, six participant Agents, manager-authored public audit labels, projection of observable native envelopes, and persistent plans/comparisons. Reference Chapters 2–3 for MCP and specialist construction.

## Actual end-to-end flow

```text
POST /api/assessments
  → AssessmentService creates/persists AssessmentRun
  → MagenticAssessmentOrchestrator creates manager AIAgent
  → six AssessmentAgentFactory.Create calls
  → each participant receives discovered MCP tools
  → MagenticWorkflowBuilder(manager)
  → AddParticipants
  → RequirePlanSignoff(false)
  → WithMaxRounds / WithMaxStalls / WithMaxResets
  → InProcessExecution.RunAsync
  → native manager/participant execution and MCP calls
  → extract public workflow envelopes and meaningful text
  → AssessmentRunState projects observed participant events
  → parse manager PLAN/REVISION/etc. labels when present
  → persist final recommendation, history, and comparison data
```

## Important code and APIs

- `MagenticAssessmentOrchestrator.cs`: native workflow construction and event extraction.
- `AssessmentAgentFactory.cs`: participant Agents and model-visible MCP functions.
- `AssessmentRunState.cs`: observed native envelope and manager-label projection.
- `EnterpriseMcpClient.cs`: discovery, read-only policy, caching, and call-count checks.
- `AssessmentService.cs`: persisted lifecycle.
- `AssessmentComparisonService.cs`: deterministic run metrics.

Anchor APIs: `MagenticWorkflowBuilder`, `AddParticipants`, `RequirePlanSignoff`, `WithMaxRounds`, `WithMaxStalls`, `WithMaxResets`, `InProcessExecution.RunAsync`, and `WorkflowOutputEvent`.

## Model-driven versus deterministic

Model-driven: initial plan, participant selection/order, repeated participation, progress evaluation, revision, conflict reconciliation, completion, participant tool choice, and recommendation.

Deterministic: available participants and tools, read-only policy, configured native limits, call caching/count checks, public-label parsing, persistence, comparison, and failure state.

`PLAN:`, `REVISION:`, and related values are manager-authored observable text requested by instructions. They are not native typed Magentic ledger objects.

## Persistence and runtime

EF migrations persist completed/failed assessment graphs, MCP calls, projected plans, revisions, events, and configuration snapshots. Persistence does not checkpoint active Magentic execution. Restart can reopen history but cannot resume an interrupted assessment. Private Magentic ledger/reasoning is neither exposed nor stored.

## Tests: exactly what they prove

Tests prove six participant types; option defaults; manager-label parsing; source use of `MagenticWorkflowBuilder` without an application `while`; projection from synthetic native envelopes; omission of missing artifacts; revision/conflict projection; open-ended objective fixtures; MCP caching and six real read-only stdio catalogs; SQLite/configuration persistence; comparison logic; and UI source wiring.

They do not execute a real Magentic workflow or Azure OpenAI, and do not prove native planning, replanning, stalls, resets, or all host budgets at runtime.

## Limitations

- `MaxAgentInvocations` is stored but not enforced by the orchestrator.
- `MaxToolCallsPerAgent` naming is stronger than the visible invocation-context isolation; avoid claiming rigorously separate per-participant accounting.
- Plans/revisions appear only if manager output contains requested labels.
- Internal native Magentic state is not publicly available as typed data.
- Projected timestamps are application observation times.
- No durable execution or live progress stream.
- MCP tool bodies remain deterministic demo integrations.

## Claims to avoid

Do not claim MAF `1.15.0`, live Magentic test execution, exposed internal ledger or chain-of-thought, guaranteed typed native plans, enforced `MaxAgentInvocations`, restart resumption, qualitative recommendation comparison, or manager labels as native event types.

## Recommended sections

1. Custom delegation versus Magentic
2. Manager and participant responsibilities
3. Six focused participants
4. Building the native Magentic workflow
5. Round, stall, and reset guardrails
6. Specialist MCP capabilities
7. Planning, delegation, progress, and revision
8. Observable output versus private state
9. Projecting native events without invention
10. Persistence and configuration snapshots
11. Deterministic run comparison
12. Test and API limitations

## Reference instead of repeating

Reference Chapter 2 for MCP mechanics and Chapter 3 for specialist Agents and custom delegation. Explain only the differences introduced by native Magentic ownership.
