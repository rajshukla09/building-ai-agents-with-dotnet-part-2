# Verified Writing Context — Part 2, Chapter 5

## Multi-Agent Orchestration Patterns

Verified against `chapter-05` and its seventeen passing tests. Current MAF package references use `1.17.0`.

## What this chapter should teach

Teach how known collaboration requirements map to explicit native topologies: sequential, concurrent, handoff, and group chat. The purpose is comparison, not declaring one universally best.

## What it adds over Chapter 4

Chapter 4 gives open-ended planning to Magentic. Chapter 5 uses explicit execution shapes through `AgentWorkflowBuilder`, then persists and compares their observable runtime projections.

## Actual flows

```text
Sequential: Security → Quality → Architecture → Release

Concurrent: Security ─┐
            Quality  ─┼→ native fan-in result
            Architecture ─┘
            → separate sequential workflow containing ReleaseAgent

Handoff: Release entry Agent ↔ Security → Architecture → Release

Group chat: Security + Quality + Architecture
            → RoundRobinGroupChatManager, maximum six iterations
```

Each HTTP endpoint executes synchronously, reads native event envelopes, creates a readable projection, persists a run graph, and returns a `ReviewResponse`.

## Important code and APIs

- `SequentialReview.cs`: `AgentWorkflowBuilder.BuildSequential`.
- `ConcurrentReview.cs`: `BuildConcurrent`, fan-in reading, separate ReleaseAgent workflow.
- `HandoffReview.cs`: `CreateHandoffBuilderWith` and four `WithHandoff` edges.
- `GroupChatReview.cs`: `CreateGroupChatBuilderWith`, `RoundRobinGroupChatManager`, turn limit.
- `WorkflowResultReader.cs` and `ReadableEventProjector.cs`: safe event text extraction and business projection.
- `ReviewExecutionService.cs`: success/failure persistence.
- `RunComparisonService.cs`: deterministic comparison metrics.

## Model-driven versus deterministic

Model-driven: Agent review text, sequential transformation, native handoff choices within configured edges, and group-chat contributions.

Deterministic: participant sets, sequential order, concurrent branches, handoff graph, round-robin manager, six-turn limit, fan-in prompt, persistence, event projection, and comparison.

## Persistence and runtime

EF migrations store completed and failed runs, raw envelopes, and reconstructed Agent windows. Execution remains inside the HTTP request; there is no background queue, checkpoint, or resumption. History survives restart. Repeated appearances of the same business Agent are grouped into one stored window. Missing native timestamps fall back to receipt time.

## Tests: exactly what they prove

Tests inspect native-builder source shape and deterministic fixtures. They prove expected builder calls, participant declarations, four handoff edges, group-chat limit, persistence of fixture graphs, metric calculations from supplied records, generated-name normalization, readable chunk projection, and UI availability of four patterns.

They do not run live MAF patterns or Azure OpenAI, prove actual branch overlap, verify runtime handoff decisions, or verify live group-chat turns.

## Limitations

- Concurrent review and final release decision are two separate workflow runs.
- Group chat contains no `ReleaseAgent`; `WorkflowResultReader` returns its fixed no-ReleaseAgent fallback instead of a parsed shared decision.
- Timing is reconstructed from observable envelopes and may use receipt time.
- Repeated Agent invocations collapse by normalized Agent name.
- Fixture overlap tests prove metric logic, not actual parallel execution.
- No retries, timeouts, durable execution, or cancellation recovery.

## Claims to avoid

Do not claim that tests execute all patterns; concurrent fixture timing proves live overlap; concurrent review and release decision are one native graph; group chat yields a parsed shared release decision; handoff uses an application supervisor loop; persisted windows are exact spans; Magentic is runnable here; or runs resume after restart.

## Recommended sections

1. Choosing topology from collaboration needs
2. Shared release-review Agents
3. Sequential orchestration
4. Concurrent fan-out/fan-in
5. The separate concurrent decision workflow
6. Handoff and transfer conditions
7. Group chat and turn management
8. Comparing the patterns
9. Reading raw native events
10. Building a readable projection
11. Persisting and comparing runs
12. Test boundaries and selection guidance

## Reference instead of repeating

Reference Chapter 4 for Magentic and Chapter 3 for custom supervisor routing. Reference Chapters 1 and 4 for general Agent configuration rather than repeating it for every participant.
