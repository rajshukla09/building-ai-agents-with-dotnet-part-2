# Code flow — release-review orchestration

The comparison page sends the same release text to any of four endpoints. Each endpoint creates narrow `AIAgent` participants, builds a native MAF 1.15.0 `Workflow`, and runs it with `InProcessExecution.RunAsync`. This chapter compares orchestration; the evidence is deliberately mocked in the prompt and there is no custom orchestration engine.

```text
MAF Pattern Execution
        ↓
Structured Run Events
        ↓
      EF Core
        ↓
      SQLite
        ↓
History / Comparison UI
```

The same task can be solved using different orchestration patterns. Persisting execution data lets us compare their behavior instead of only reading conceptual diagrams. The persistence adapter records only public workflow event envelopes; it does not fabricate unobservable orchestration events or request chain-of-thought.

## When to use what

| Pattern | Use when |
| --- | --- |
| Sequential | Each step depends on the previous result |
| Concurrent | Independent tasks can run in parallel |
| Handoff | One specialist should transfer control to another |
| Group Chat | Specialists need to collaborate/discuss |
| Magentic | The problem requires planning, delegation and replanning |

**Fan-out = distribute work. Fan-in = collect/aggregate parallel results.**

## The four runnable patterns

- **Sequential:** `AgentWorkflowBuilder.BuildSequential` sends the accumulated result through Security → Quality → Architecture → Release. Use it when downstream judgment must incorporate upstream findings.
- **Concurrent:** `AgentWorkflowBuilder.BuildConcurrent` fans the same request out to Security, Quality, and Architecture. Native workflow output fans their independent results in; a Release workflow then receives that collection and decides.
- **Handoff:** `AgentWorkflowBuilder.CreateHandoffBuilderWith`, `WithHandoff`, and `Build` let Release transfer control to Security, Security optionally transfer to Architecture, and specialists return control to Release. There is no supervisor loop.
- **Group Chat:** `AgentWorkflowBuilder.CreateGroupChatBuilderWith` receives the manager factory and `AddParticipants` adds the specialists to the shared conversation. The factory constructs a `RoundRobinGroupChatManager` from MAF's final participant list; `MaximumIterationCount = 6` keeps the discussion short while allowing every specialist to participate and build on earlier messages.

## Magentic: reference, not a fifth application

Choose Magentic when an open-ended objective requires the system to plan, delegate, evaluate progress, and potentially replan. Chapter 4 contains the complete native `MagenticWorkflowBuilder` example; duplicating that larger assessment here would obscure this chapter's comparison.

## Try it

Configure `AzureOpenAI` in `src/SoftwareReleaseReview.Api/appsettings.json`, run `dotnet run --project src/SoftwareReleaseReview.Api`, and use Swagger with:

```json
{
  "release": "Release 4.8 contains authentication and payment changes."
}
```

Run the Web project to open the **Orchestration Patterns** comparison page. It explains and diagrams the selected pattern, calls `POST /api/reviews/sequential`, `/concurrent`, `/handoff`, or `/group-chat`, and shows the decision plus native workflow event type, executor, and text projections so the different execution shapes can be observed. Magentic intentionally has no control on this page; Chapter 4 owns that experience.

Every execution is persisted to `App_Data/reviews.db`. `GET /api/reviews/runs` returns history, `GET /api/reviews/runs/{id}` reopens details, and `GET /api/reviews/compare?ids=id1,id2` calculates deterministic metrics for two to four runs. The comparison uses stored timestamps and event types—never another LLM call.

The database retains raw MAF envelopes for future observability work. The ordinary UI uses a separate readable projection: generated executor suffixes are normalized, infrastructure executors are excluded, streaming response chunks are joined into one contribution, and only the final `ReleaseAgent` contribution appears as the release decision. Concurrent metrics count only Security, Quality, and Architecture as fan-out branches.
