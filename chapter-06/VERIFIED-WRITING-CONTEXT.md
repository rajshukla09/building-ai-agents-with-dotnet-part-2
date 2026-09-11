# Verified Writing Context — Part 2, Chapter 6

## Durable Workflows

Verified against `chapter-06`. Two tests were reported as passed, but both silently return when no scheduler accepts a TCP connection on port 8080; this is not an xUnit skip and may provide no durable-runtime evidence.

## What this chapter should teach

Teach native durable MAF execution through the Durable Task extension: register a MAF workflow with a durable worker/client, convert `RequestPort` into an external-event wait, recreate the API/worker host, resume the same orchestration instance, separate checkpoint ownership from business audit data, and make external effects idempotent.

## What it adds over Chapter 5

Chapter 5 persists completed history but not execution position. Chapter 6 introduces Durable Task Scheduler, durable instance IDs, external events, native continuation after host recreation, and idempotent procurement. Explicitly contrast this with Chapter 1’s application-managed decision-only continuation.

## Actual end-to-end flow

```text
POST /api/purchases
  → insert PurchaseRun in SQLite
  → IWorkflowClient.RunAsync with GUID instance ID
  → ValidatePurchase
  → BudgetCheck
  → AwaitManagerApproval
  → RequestPort<ApprovalRequest, ApprovalDecision>
  → durable adapter publishes pending external-event name
  → PurchaseService polls custom status
  → API returns WaitingForApproval
  → host may stop and be recreated
  → approve/reject reads generated event name
  → DurableTaskClient.RaiseEventAsync
  → same orchestration resumes at Procurement
  → business outcome and idempotent procurement record saved
  → durable instance completes
```

## Important code and APIs

- `PurchaseWorkflow.cs`: typed executors, `RequestPort`, and graph.
- `PurchaseService.cs`: `IWorkflowClient.RunAsync`, metadata polling, pending-event extraction, `RaiseEventAsync`, and completion wait.
- `Program.cs`: `ConfigureDurableWorkflows`, `AddWorkflow`, worker/client `UseDurableTaskScheduler`.
- `PurchasesDb.cs`: business audit data and unique idempotency key.
- `DurablePurchaseTests.cs`: conditional external integration test.

Packages: Workflows `1.17.0`, DurableTask integration preview `1.16.0-preview.260730.1`, Durable Task client/worker `1.25.0`.

## Model-driven versus deterministic

There is no AI or model behavior. Validation, the 100,000 budget threshold, approval wait, manager decision, procurement, status mapping, and idempotency are deterministic. This isolates durability from model variability.

## Persistence and runtime

Durable Task Scheduler owns orchestration history, position, pending event, runtime status, resumption, and output. SQLite owns purchase details, counters, business status, order reference, and procurement idempotency. SQLite never selects or reconstructs continuation.

## Tests: exactly what they prove

When a compatible scheduler is available, the tests exercise host recreation, waiting-state recovery, approval resumption, one-time validation/budget/procurement counters, repeated approval, and terminal rejection.

When the scheduler is unavailable, `SchedulerAvailable()` causes an immediate return. Reported passes therefore do not prove those behaviors unless scheduler participation is separately confirmed.

## Limitations

- External scheduler and preview integration package required.
- Scheduler check verifies only that TCP port 8080 accepts a connection.
- Missing infrastructure is reported as passed, not skipped.
- `Start` always returns `WaitingForApproval`, even after an early completion/failure.
- Polling has no explicit timeout; `Decide` waits synchronously for completion.
- No approval expiry, cancellation API, authentication, authorization, or manager validation.
- Business writes and orchestration checkpoints are not atomic.
- Idempotency limits duplicate procurement writes; it is not an exactly-once distributed guarantee.
- SQLite uses `EnsureCreated`, not migrations.

## Claims to avoid

Do not claim that the observed test run proves restart durability; unavailable scheduler tests are skipped; SQLite stores checkpoints; event names are hard-coded; a decision-only graph is reconstructed; AI participates; external effects are exactly once; expiry/cancellation exist; or every start response reflects early workflow failure.

## Recommended sections

1. History persistence versus durable execution
2. Chapter 1 continuation versus native durability
3. Durable Task Scheduler architecture
4. Building the purchase graph
5. Registering durable MAF workflows
6. `RequestPort` as an external-event wait
7. Reading and raising the generated event
8. Host recreation and same-instance resumption
9. Scheduler state versus SQLite business state
10. Idempotent procurement and replay
11. Testing with real infrastructure
12. Preview and operational limitations

## Reference instead of repeating

Reference Chapter 1 for the typed approval boundary, then focus on what native durable recovery changes. Reference Chapter 5 for ordinary in-process workflow execution.
