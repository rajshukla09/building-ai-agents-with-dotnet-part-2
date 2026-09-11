# Chapter 1 code flow: Human-in-the-Loop claims review

This document describes the code that is currently in `chapter-01`. It is not a generic Microsoft Agent Framework (MAF) design. In particular, the installed `Microsoft.Agents.AI.Workflows` 1.15.0 package does **not** expose a serializable `StreamingRun` checkpoint through the API used here. The sample therefore releases the original streaming run and later executes a small, persisted-data-driven MAF continuation. The consequences of that choice are called out throughout the document and summarized under [Implementation notes and remaining limitations](#implementation-notes-and-remaining-limitations).

## 1. Big picture

```text
Blazor WebAssembly client
        │ HTTP
        ▼
ASP.NET Core controllers
        │
        ├── ClaimWorkflowService.StartAsync
        │       ├── persist ClaimWorkflowRunRecord (Queued)
        │       └── enqueue ClaimWorkflowQueueItem.Start
        │
        ▼
in-memory IClaimWorkflowQueue (Channel<ClaimWorkflowQueueItem>)
        │
        ▼
ClaimWorkflowBackgroundService (fresh DI scope per item)
        │
        ▼
ClaimWorkflowService.ExecuteAsync
        │
        ▼
MAF ClaimReviewWorkflow
        │
        ├── ClaimIntakeAgentExecutor
        ├── ClaimValidationExecutor
        ├── RiskAssessmentAgentExecutor
        ├── HumanApprovalRequestExecutor
        ├── native RequestPort<ClaimApprovalRequest, ClaimApprovalDecision>
        └── ClaimDecisionExecutor
                    │
                    │ RequestInfoEvent interrupts the first execution period
                    ▼
        persist ClaimApprovalRecord (Pending)
        update run to WaitingForApproval
        dispose StreamingRun and release worker scope
                    │
                    │ reviewer decides later
                    ▼
Approval API → persist Approved/Rejected → enqueue Resume
                    │
                    ▼
ClaimWorkflowBackgroundService (new DI scope)
        │
        ▼
ClaimWorkflowService.ResumeAsync
        │
        ▼
MAF decision continuation → ClaimDecisionExecutor
        │
        ▼
persist final decision and Completed/Rejected status
```

The SQL records connect the two execution periods. The original MAF `StreamingRun`, its callback, and its DI scope are not retained while a reviewer is deciding.

> **Core mental model:** waiting for a human is a persisted workflow state, not a running thread and not an exception.

## 2. Why there is a background service

The agents are **not** .NET `BackgroundService` implementations. `ClaimWorkflowBackgroundService` is an execution host and queue consumer; MAF remains responsible for invoking the workflow executors.

```text
ClaimWorkflowBackgroundService
        │ hosts an execution
        ▼
MAF workflow
        │ orchestrates nodes
        ▼
agents and deterministic executors
```

Running the workflow in the submission HTTP request would couple request latency to intake, validation, risk assessment, and a human response:

```text
Bad
HTTP request → run agents → wait for reviewer → eventually return
```

The implemented path persists and queues first:

```text
Preferred
HTTP request → persist Queued run → enqueue Start → return 202 + WorkflowRunId
                                      │
                                      ▼
                         background worker executes MAF
```

`ClaimWorkflowService.StartAsync` is the producer. `ClaimWorkflowBackgroundService.ExecuteAsync` consumes `ClaimWorkflowQueueItem.Start` and `.Resume` values from `IClaimWorkflowQueue`. Each item is awaited and handled in a new dependency-injection scope, so an EF `ClaimsDbContext` is neither shared between executions nor retained while approval is pending.

## 3. Complete claim-submission flow

The HTTP entry point is `POST /api/claim-workflows` in `ClaimWorkflowsController.Start`.

1. Model binding creates a `ClaimSubmissionRequest`.
2. The controller calls `IClaimWorkflowService.StartAsync`.
3. `StartAsync` creates a new `WorkflowRunId`.
4. It inserts a `ClaimWorkflowRunRecord` containing the claim summary, serialized original request, timestamps, and `Status = "Queued"`.
5. After the database save succeeds, it calls `IClaimWorkflowQueue.EnqueueStartAsync(id, request, ...)`.
6. It returns `StartClaimWorkflowResponse(id, "Queued")`.
7. The controller returns `202 Accepted` with a location pointing to `GET /api/claim-workflows/{id}`.

The important ordering is:

```csharp
await db.SaveChangesAsync(cancellationToken);
await workflowQueue.EnqueueStartAsync(id, request, cancellationToken);
```

A queue item therefore refers to an already-persisted run.

The background half is:

1. `ClaimWorkflowBackgroundService.ExecuteAsync` reads the channel.
2. A `Start` item is passed to `RunStartAsync`.
3. `RunStartAsync` creates an async DI scope.
4. It resolves the scoped `IClaimWorkflowService` implementation and calls the internal `ClaimWorkflowService.ExecuteAsync`.
5. `ExecuteAsync` changes the run to `Running`, appends `WorkflowStarted`, creates `ClaimReviewWorkflow`, and calls `InProcessExecution.RunStreamingAsync` with `WorkflowStartMessage`.
6. It enumerates `streamingRun.WatchStreamAsync()` so intermediate `RequestInfoEvent` instances are observed instead of waiting only for final output.
7. The method returns either a `WaitingForApproval`, `Completed`, or `Failed` `WorkflowExecutionOutcome`.
8. `RunStartAsync` returns, and disposing its scope releases the scoped workflow service, executors, and `ClaimsDbContext`.

The worker logs a `Failed` outcome as an error, but a `WaitingForApproval` outcome is simply allowed to return; suspension is not logged or persisted as failure.

## 4. Actual MAF workflow topology

`ClaimReviewWorkflow.Create()` builds this graph:

```text
WorkflowStartMessage
        │
        ▼
ClaimIntakeAgentExecutor
        │ ClaimDraftMessage
        ▼
ClaimValidationExecutor
        │ ValidatedClaimMessage
        ▼
RiskAssessmentAgentExecutor
        │ RiskAssessmentMessage
        ▼
HumanApprovalRequestExecutor
        │ ClaimApprovalRequest
        ▼
RequestPort<ClaimApprovalRequest, ClaimApprovalDecision>
        │ ClaimApprovalDecision
        ▼
ClaimDecisionExecutor
        │ ClaimDecisionResponse
        ▼
WorkflowOutputEvent
```

The request port has the stable port ID `claim-human-approval` and is created with:

```csharp
RequestPort.Create<ClaimApprovalRequest, ClaimApprovalDecision>(
    "claim-human-approval")
```

### Executor responsibilities

| Executor | Input → output | Work performed | AI or deterministic | Persistence and failure behavior |
|---|---|---|---|---|
| `ClaimIntakeAgentExecutor` | `WorkflowStartMessage` → `ClaimDraftMessage` | Calls `IClaimIntakeAgent`, trims/normalizes submission fields, and creates a draft. The sample agent refuses descriptions containing `REFUSE`. | Agent abstraction; the supplied implementation is deterministic C#. | The common `ClaimExecutor` base records start/completion events and an executor trace. Agent failure becomes `InvalidOperationException` and reaches the workflow boundary. |
| `ClaimValidationExecutor` | `ClaimDraftMessage` → `ValidatedClaimMessage` | Applies required-field, supported-type, date, amount, and evidence rules. | Deterministic `IClaimValidator`. | Throws `ClaimValidationException` for invalid input. `ExecuteAsync` persists `ValidationFailed`, `FailureStage = ClaimValidationExecutor`, `Error`, and `CompletedAt`. |
| `RiskAssessmentAgentExecutor` | `ValidatedClaimMessage` → `RiskAssessmentMessage` | Calls `IRiskAssessmentAgent`; the sample scores amount, fraud text, and validation warnings and derives Low/Medium/High risk. | Agent abstraction; supplied implementation is deterministic C#. | Adds the explicit `RiskAssessmentCompleted` event in addition to base executor events. Agent/schema failure propagates to the workflow boundary. |
| `HumanApprovalRequestExecutor` | `RiskAssessmentMessage` → `ClaimApprovalRequest` | Evaluates `IHumanApprovalPolicy`, creates the application approval ID, copies the validated claim/risk result, and assigns requested/expiry times. | Deterministic policy/integration node. | Base executor trace/events are persisted. It does not itself insert the approval row; that occurs only after MAF emits `RequestInfoEvent`. |
| native request port | `ClaimApprovalRequest` → `ClaimApprovalDecision` | Converts the typed request into MAF's external-input boundary and emits `RequestInfoEvent`. | Native MAF workflow primitive. | The streaming host captures and persists the request. No exception represents normal waiting. |
| `ClaimDecisionExecutor` | `ClaimApprovalDecision` → `ClaimDecisionResponse` | Reloads the approval, validates both correlation IDs, deserializes the persisted validated-claim snapshot, and creates the final approved or rejected decision. | Deterministic final node. | Approved pays the claimed amount and returns `Completed`; rejected pays zero and returns `Rejected`. Invalid correlation or snapshot data throws and is a technical failure. |

`ClaimExecutor<TIn,TOut>` wraps every executor with `${ExecutorName}Started` and `${ExecutorName}Completed` events plus `ClaimExecutorTraceRecord`. On exceptions it writes a failed/cancelled trace and rethrows. `WorkflowEventSequenceLock` serializes sequence allocation per workflow and retries the SQLite unique-index collision once.

## 5. Typed messages moving through the graph

| Message | Produced by | Consumed by | Purpose and important fields |
|---|---|---|---|
| `WorkflowStartMessage` | `ClaimWorkflowService.ExecuteAsync` | `ClaimIntakeAgentExecutor` | Carries `WorkflowRunId` and the original `ClaimSubmissionRequest`. |
| `ClaimDraftMessage` | `ClaimIntakeAgentExecutor` | `ClaimValidationExecutor` | Carries the run ID, original request, and normalized `ClaimDraft`. |
| `ValidatedClaimMessage` | `ClaimValidationExecutor` | `RiskAssessmentAgentExecutor` | Carries the immutable `ValidatedClaim` plus validation warnings and original request. |
| `RiskAssessmentMessage` | `RiskAssessmentAgentExecutor` | `HumanApprovalRequestExecutor` | Adds `ClaimRiskAssessment` while retaining claim, warnings, original request, and run ID. |
| `ClaimApprovalRequest` | `HumanApprovalRequestExecutor` | native typed request port | Contains `ApprovalRequestId`, `WorkflowRunId`, validated claim, risk assessment, approval reason/rules, `RequestedAt`, and `ExpiresAt`. |
| `ClaimApprovalDecision` | Approval controller/recovery code | continuation `ClaimDecisionExecutor` | Contains both correlation IDs, Approved/Rejected outcome, reviewer, comment, and decision time. |
| `ClaimDecisionResponse` | `ClaimDecisionExecutor` | MAF workflow output and `ClaimWorkflowService` | Contains run/status, final `ClaimDecisionDto`, workflow summary, and approval ID. |
| `NativeApprovalResponseCommand` | `ClaimApprovalsController` or startup recovery | resume queue and `ResumeAsync` | Wraps run ID, approval ID, and typed decision for asynchronous continuation. Despite its historical name, current code does not send the command to a live native request. |

All workflow messages implement or carry `IWorkflowRunMessage` identity so executor event persistence can correlate work with the same logical run.

## 6. When human approval is triggered

`HumanApprovalPolicy.Evaluate` uses `HumanApprovalOptions` (defaults and `appsettings.json`: enabled, amount threshold 50,000, high-risk score threshold 70, timeout 30 minutes). It records a human-review rule when any of these are true:

- claimed amount is greater than the configured amount threshold;
- risk level is `High`;
- risk score is at least the configured high-risk threshold;
- validation produced warnings.

If no rule matches, it returns `IsRequired = false` and the reason `Low-risk claim can proceed without human approval.` If disabled, it likewise returns `IsRequired = false`.

### Current topology discrepancy

The graph does **not** branch on `HumanApprovalRequirement.IsRequired`. `HumanApprovalRequestExecutor` always creates a `ClaimApprovalRequest`, and its only outgoing edge always enters the request port. Consequently **every valid claim currently pauses for a human**, even when the policy says approval is not required or approval is disabled. The reason and triggered rules are preserved, but `IsRequired` is not carried in `ClaimApprovalRequest` and cannot select an automatic path. This is actual current behavior, not the intended business diagram with a “No → continue” branch.

## 7. Native MAF human approval and correlation IDs

When the request port receives `ClaimApprovalRequest`, MAF emits a `RequestInfoEvent`. Its `Request.Data` may be the typed object or a `PortableValue`; `MafApprovalPayload.Deserialize` supports both. With MAF 1.15.0, the helper serializes the portable representation and handles the shapes the package actually exposes rather than using a nonexistent `PortableValue.ToObject<T>()` API.

Three identifiers are involved:

```text
WorkflowRunId (application logical run)
     │
     └── ApprovalRequestId (ClaimApprovals primary key)
             │
             └── MafRequestId (native request identifier/audit correlation)
```

- **`WorkflowRunId`** joins the run, its events/traces, and approvals across both execution periods.
- **`ApprovalRequestId`** is generated by `HumanApprovalRequestExecutor`, is the API-facing approval identity, and is the primary key of `ClaimApprovalRecord`.
- **`MafRequestId`** comes from `RequestInfoEvent.Request.RequestId`. It records which native request produced the database approval.

Portable payloads may lose/default application GUIDs. `MafApprovalPayload.NormalizeCorrelation` replaces a missing workflow ID with the active run ID and derives a stable approval GUID from the native request ID when needed. The streaming host always sets the persisted record's `WorkflowRunId` to its active run and stores the original `MafRequestId`.

In the current continuation architecture, `MafRequestId` is retained for audit/correlation; it is **not** used to restore or respond to the disposed native request.

## 8. Suspension: what actually happens

`ClaimWorkflowService.ExecuteAsync` watches every streaming event. On `RequestInfoEvent` it:

1. obtains `Request.Data` and `RequestId`;
2. safely deserializes and normalizes the typed `ClaimApprovalRequest`;
3. creates or updates `ClaimApprovalRecord`;
4. stores the MAF ID, reason/rules, validated claim JSON, risk JSON, timestamps, and `Status = "Pending"`;
5. normalizes the approval window so it is at least the configured timeout;
6. sets `ClaimWorkflowRunRecord.Status = "WaitingForApproval"` and clears `Error`;
7. saves the database transaction represented by that `SaveChangesAsync` call;
8. appends `ApprovalRequested` with both approval IDs in event data;
9. returns `WorkflowExecutionOutcome.Waiting(...)` immediately.

The `finally` block disposes the streaming run (when it implements `IAsyncDisposable`), records total elapsed time, and returns control to `RunStartAsync`. The background method and its DI scope then finish. There is no registry, live response delegate, approval wait task, or SQL polling loop retaining the workflow.

There is also **no persisted native MAF checkpoint**. The durable information is the application state needed by the later decision-only continuation: approval decision/correlation, claim snapshot, risk snapshot, workflow record, and event history.

## 9. Database persistence

SQLite is accessed through `ClaimsDbContext`.

### `ClaimWorkflowRunRecord` / `ClaimWorkflowRuns`

This is the logical workflow aggregate. Important fields are:

- `WorkflowRunId`;
- claim summary fields used by list/status UI;
- `Status`;
- `StartedAt`, `CompletedAt`, and duration fields;
- `OriginalRequestJson`;
- `FinalDecisionJson`;
- `FailureStage` and `Error`.

The approval path is:

```text
Queued → Running → WaitingForApproval → Running → Completed
                                              └──→ Rejected
```

Other terminal/interruption states used in code are `ValidationFailed`, `Failed`, `Cancelled`, and `Expired`.

### `ClaimApprovalRecord` / `ClaimApprovals`

This is the human work item and continuation snapshot. Relevant data includes:

- application `ApprovalRequestId` and parent `WorkflowRunId`;
- native `MafRequestId`;
- `Pending`, `Approved`, `Rejected`, `Expired`, or `Cancelled` status;
- serialized validated claim, risk assessment, rules, and warnings;
- normalized reason;
- reviewer/comment and requested/expiry/decision times;
- `Version`, configured as an EF concurrency token.

Its usual lifecycle is `Pending → Approved`, `Pending → Rejected`, or `Pending → Expired`.

### `ClaimWorkflowEventRecord` / `ClaimWorkflowEvents`

This append-oriented timeline stores a per-workflow sequence, event type, stage, status, summary, optional JSON data, and timestamp. A unique `(WorkflowRunId, Sequence)` index provides ordering for the live page and API replay.

### Traces and diagnostic entities

`ClaimExecutorTraceRecord` stores executor status, duration, and error. The schema also defines `ClaimAgentExecutionRecord` and `ClaimMessageTransitionRecord`; the current Chapter 1 execution path shown here does not add those two record types.

### No checkpoint table

There is no current checkpoint/state entity and no `CheckpointJson` property on `ClaimApprovalRecord`. Startup schema code only ensures `MafRequestId` exists for older SQLite files. The “checkpoint” for this sample is therefore not a serialized MAF runtime: it is the persisted application snapshots consumed by `ClaimDecisionExecutor`.

## 10. What happens while waiting

`WaitingForApproval` means:

- the run and approval exist in SQLite;
- the approval appears through the approvals API/UI until decided or expired;
- the original streaming run is disposed;
- the worker call and scoped services have returned;
- no agent or executor is continuously running;
- no delegate or workflow object must remain in memory;
- no server polling is needed to preserve the suspension.

The Blazor live page does poll the status/events API for display updates. That polling observes persisted state; it does not keep the workflow alive or cause continuation.

## 11. Human approval API flow

The UI sends either:

- `POST /api/claim-approvals/{id}/approve`, or
- `POST /api/claim-approvals/{id}/reject`.

Both actions enter `ClaimApprovalsController.Decide`:

1. `IClaimApprovalStore.GetAsync` verifies the approval exists and supplies its workflow correlation.
2. The controller builds a typed `ClaimApprovalDecision` with the URL approval ID, persisted workflow ID, selected outcome, reviewer/comment, and current timestamp.
3. `EfClaimApprovalStore.DecideAsync` rejects missing/expired/conflicting decisions and persistently changes a pending row to `Approved` or `Rejected`.
4. For an accepted decision, the controller creates `NativeApprovalResponseCommand` and calls `IClaimWorkflowQueue.EnqueueResumeAsync`.
5. It returns `202 Accepted` with `QueueResumeResponse(..., "ResumeQueued")`.

A repeated identical decision returns `AlreadyDecided`; the controller re-enqueues it and returns `200 OK`. This supports recovery if an earlier in-memory queue item was lost. A conflicting outcome/reviewer is `409 Conflict`; an expired item is `410 Gone`; a missing item is `404 Not Found`.

The controller does not execute MAF or the final executor directly.

## 12. Workflow resume

`ClaimWorkflowBackgroundService` reads `ClaimWorkflowQueueItem.Resume`, creates a new scope, resolves `IClaimWorkflowService`, and calls `ResumeAsync`.

`ResumeAsync`:

1. loads the run and approval;
2. validates the command's workflow ID, approval ID, run status (`WaitingForApproval`), and persisted decision status;
3. returns `null` for stale, duplicate-after-completion, or mismatched work, preventing a second continuation;
4. appends `ApprovalDecisionReceived`;
5. changes the run to `Running` and appends `WorkflowResumed`;
6. runs `InProcessExecution.RunAsync(factory.CreateDecisionContinuation(), command.Decision)`;
7. extracts the final `ClaimDecisionResponse` from `WorkflowOutputEvent`;
8. persists `FinalDecisionJson`, `CompletedAt`, duration fields, final `Completed` or `Rejected` status, and `WorkflowCompleted`.

`CreateDecisionContinuation()` is a new MAF workflow containing **only** `ClaimDecisionExecutor`. Intake, validation, risk assessment, and approval-request executors do not rerun. `ClaimDecisionExecutor` reloads the persisted claim snapshot by approval ID.

Approval and rejection traverse the same continuation node. The outcome changes its output:

- **Approved:** status `Completed`, approved amount equals claimed amount.
- **Rejected:** status `Rejected`, approved amount is zero.

This is not `StreamingRun.SendResponseAsync`, native checkpoint restoration, or continuation of the same runtime object. It is a second MAF execution period for the same application `WorkflowRunId`.

## 13. Application restart behavior

Before restart, a waiting run has:

```text
SQLite
 ├── ClaimWorkflowRunRecord (WaitingForApproval + OriginalRequestJson)
 ├── ClaimApprovalRecord (Pending + claim/risk snapshots + MafRequestId)
 └── workflow events and executor traces

Memory
 └── no StreamingRun, callback, delegate, or retained DI scope
```

After restart, the reviewer can submit a decision normally. The controller loads the approval, persists the decision, and enqueues a new resume command. The worker starts the decision continuation from the database-backed typed decision and claim snapshot.

`ClaimWorkflowBackgroundService.RecoverDecidedApprovalsAsync` also runs once at worker startup. It finds `WaitingForApproval` runs whose approvals are already `Approved` or `Rejected`, reconstructs `ClaimApprovalDecision`, and re-enqueues the continuation. This closes the common crash window between persisting a decision and consuming its in-memory resume message.

This is safer than an in-memory callback registry because pending approvals remain actionable after process loss and no suspended workflow object consumes memory. However, it is application-level recovery, not restoration of a native MAF checkpoint.

## 14. `ClaimWorkflowBackgroundService` in detail

`ClaimWorkflowBackgroundService` **is**:

- the single reader of the workflow channel;
- an asynchronous execution host;
- responsible for creating and disposing one scope per start/resume item;
- responsible for startup recovery of decided, waiting approvals;
- the place where execution outcomes are logged.

It **is not**:

- an AI agent;
- the MAF graph/orchestrator;
- the Human-in-the-Loop request mechanism;
- persistent workflow state;
- a task that waits for a reviewer.

```text
BackgroundService
      │ hosts
      ▼
MAF execution
      │ orchestrates
      ▼
executors → agent abstractions and deterministic services
```

The channel is configured with one reader. The worker awaits each item, so Chapter 1 currently processes workflow items serially. When start reaches `WaitingForApproval`, `ExecuteAsync` returns and the next queue item can run.

## 15. `IClaimWorkflowQueue`

`IClaimWorkflowQueue` exposes:

- `EnqueueStartAsync(Guid, ClaimSubmissionRequest, ...)`;
- `EnqueueResumeAsync(NativeApprovalResponseCommand, ...)`;
- `ReadAllAsync(...)`.

The concrete `ClaimWorkflowQueue` uses an unbounded in-memory `Channel<ClaimWorkflowQueueItem>` with `SingleReader = true` and multiple writers. Its discriminated item types are:

```text
ClaimWorkflowQueueItem.Start  (WorkflowRunId + original request)
ClaimWorkflowQueueItem.Resume (NativeApprovalResponseCommand)
```

The queue decouples HTTP response time from MAF execution, but **queue messages do not survive process termination**. The persisted decision recovery partially compensates for resume messages. There is no corresponding startup recovery for a `Queued` start item, so a crash after saving a new run but before/while consuming its start message can leave that run queued. A production evolution would use a durable broker/outbox (for example Azure Service Bus plus transactional dispatch), not merely replace the channel without addressing atomicity.

## 16. Approval expiration

`ApprovalExpiryService` is a separate hosted service. Every 30 seconds it:

1. creates a new scope;
2. loads pending approvals;
3. filters `ExpiresAt <= now` in memory because SQLite has limited `DateTimeOffset` translation support;
4. changes each approval to `Expired`;
5. changes its workflow run to `Expired`;
6. saves once for the tick.

```text
Pending approval → ExpiresAt reached → approval Expired → workflow Expired
```

This periodic maintenance is not a loop that holds or resumes a suspended MAF run. Its only job is to transition stale persisted business records.

## 17. Error handling

| Failure | Persisted behavior |
|---|---|
| Validation errors | `ValidationFailed`, `FailureStage = ClaimValidationExecutor`, joined validation message in `Error`, and `CompletedAt`. |
| Intake/risk/MAF/database/other technical error during start | General catch sets `Failed`, `Error`, and `CompletedAt`. A failed newly-added approval entry is detached before saving failure state so EF does not retry the same bad insert. |
| Executor error | `ClaimExecutorTraceRecord.Status = Failed` (or `Cancelled` for cancellation), duration, error; exception propagates. |
| Approval ID/workflow ID/status mismatch during resume | `ResumeAsync` returns `null`; no continuation executes. |
| Missing/expired/conflicting approval at HTTP boundary | `404`, `410`, or `409`; no accepted resume is queued. |
| Cancellation endpoint | Run becomes `Cancelled`, pending approvals become `Cancelled`, and `WorkflowCancelled` is appended. |
| Expiry timer | Run and approval become `Expired`; this is a business terminal state, not `Failed`. |
| Continuation exception | It propagates out of `RunResumeAsync`; unlike start processing, that path currently has no local error-to-`Failed` persistence/logging wrapper. This is a remaining gap. |

Human suspension never throws `WorkflowSuspendedException`; no such type is used in this flow.

## 18. Observability and UI reconstruction

Persisted event names include:

- `WorkflowStarted`;
- `${ExecutorName}Started` and `${ExecutorName}Completed`;
- `RiskAssessmentCompleted`;
- `ApprovalRequested`;
- `ApprovalDecisionReceived`;
- `WorkflowResumed`;
- `WorkflowCompleted`;
- `WorkflowCancelled`.

Events carry ordered sequence numbers, stage/status, summaries, timestamps, and sometimes JSON correlation data. `GET /api/claim-workflows/{id}/events?afterSequence=N` returns them in sequence order, allowing the live page to replay history after refresh and request only later events.

The UI also gets the current run through `GET /api/claim-workflows/{id}`. Because SQLite cannot order `DateTimeOffset` directly in this query, the controller materializes the workflow's approval timestamps and selects the newest in memory. Pending, detail, and approved-claims pages use the approval endpoints. Although `Program` maps a SignalR hub, the current live page uses periodic HTTP status/event polling; persisted events, not the browser subscription, are authoritative.

## 19. End-to-end example

Consider:

```text
Policy:   POL-10021
Claimant: Anita Sharma
Type:     VehicleDamage
Amount:   12,000
Evidence: damage-photo.jpg
```

1. The UI posts the request to `POST /api/claim-workflows`.
2. `StartAsync` writes a run with `Status = Queued`, enqueues `Start`, and the API returns `202` plus its ID.
3. The worker changes it to `Running` and starts the full MAF graph.
4. `ClaimIntakeAgentExecutor` normalizes the input.
5. `ClaimValidationExecutor` accepts the supported type, positive amount/date, and required vehicle photo.
6. `RiskAssessmentAgentExecutor` computes a score of 12 and Low risk when there are no other factors.
7. `HumanApprovalPolicy` says this low-risk claim can proceed without human approval.
8. **Because the current graph has no conditional bypass,** `HumanApprovalRequestExecutor` still emits a request and the native port emits `RequestInfoEvent`.
9. The service persists a `Pending` approval with a 30-minute window, marks the run `WaitingForApproval`, disposes the stream, and the worker scope exits.
10. Twenty minutes later, Anita's reviewer opens the pending approval and clicks Approve.
11. The approval API persists `Approved`, queues `Resume`, and returns `ResumeQueued`.
12. The worker validates the persisted correlations, sets the run back to `Running`, and starts the decision-only MAF continuation.
13. `ClaimDecisionExecutor` reloads the validated-claim snapshot and returns an Approved decision for 12,000.
14. The service stores the final decision, records `WorkflowCompleted`, and sets the run to `Completed`.
15. Refreshing the live or approved-claims UI reads the final state from SQLite.

A rejection follows steps 1–12, but the decision executor emits Rejected with zero approved amount and the run ends as `Rejected`.

## 20. Sequence diagram

```mermaid
sequenceDiagram
    actor Reviewer
    participant UI as Blazor UI
    participant API as Controllers
    participant DB as SQLite
    participant Queue as In-memory Channel
    participant Worker as ClaimWorkflowBackgroundService
    participant Service as ClaimWorkflowService
    participant MAF as MAF Workflow

    UI->>API: POST /api/claim-workflows
    API->>Service: StartAsync(request)
    Service->>DB: INSERT run (Queued)
    Service->>Queue: Enqueue Start
    API-->>UI: 202 + WorkflowRunId

    Worker->>Queue: Read Start
    Worker->>Service: ExecuteAsync (new scope)
    Service->>DB: run = Running; WorkflowStarted
    Service->>MAF: RunStreamingAsync(full graph)
    MAF->>MAF: Intake → validation → risk → approval request executor
    MAF-->>Service: RequestInfoEvent
    Service->>DB: INSERT approval (Pending)
    Service->>DB: run = WaitingForApproval; ApprovalRequested
    Service-->>MAF: Dispose StreamingRun
    Service-->>Worker: WaitingForApproval outcome
    Worker-->>Worker: Return and dispose scope

    Note over DB,Worker: No workflow object or task waits for the reviewer

    Reviewer->>UI: Approve or Reject
    UI->>API: POST /api/claim-approvals/{id}/{decision}
    API->>DB: Validate and persist decision
    API->>Queue: Enqueue Resume
    API-->>UI: 202 ResumeQueued

    Worker->>Queue: Read Resume
    Worker->>Service: ResumeAsync (new scope)
    Service->>DB: Load run + approval; run = Running
    Service->>MAF: RunAsync(decision-only continuation)
    MAF->>DB: ClaimDecisionExecutor loads claim snapshot
    MAF-->>Service: ClaimDecisionResponse
    Service->>DB: Final decision + Completed/Rejected + events
```

The resume arrow deliberately says “decision-only continuation,” not “restore checkpoint” or `SendResponseAsync`, because that is what this version implements.

## 21. Who owns what?

| Component | Responsibility |
|---|---|
| `ClaimWorkflowsController` | Accept starts, return workflow state/events, and request cancellation. |
| `ClaimApprovalsController` | List/detail approvals, validate HTTP decisions through the store, and enqueue resume work. |
| `ClaimWorkflowService` | Persist lifecycle transitions; launch/watch the full MAF run; capture native requests; run the persisted decision continuation. |
| `IClaimWorkflowQueue` / `ClaimWorkflowQueue` | Decouple API producers from one in-process execution consumer. |
| `ClaimWorkflowBackgroundService` | Recover decided waiting records, consume queue items, and own a fresh scope for each execution period. |
| `ClaimReviewWorkflow` | Define the full MAF graph and decision-only continuation graph. |
| Agent abstractions | Perform intake and risk reasoning (deterministic implementations in this sample). |
| Executors | Adapt typed messages, invoke agents/rules, and persist execution events/traces. |
| `ClaimsDbContext` / SQLite | Durable business state, decisions, snapshots, events, and traces. |
| `EfClaimApprovalStore` | Approval queries, expiry/idempotency/conflict checks, and decision persistence. |
| `ApprovalExpiryService` | Periodically expire stale persisted approvals and runs. |
| Blazor pages | Submit claims, poll/view live status, and review pending/approved claims. |

## 22. What runs where?

```text
.NET host
│
├── ASP.NET Core API
│   ├── ClaimWorkflowsController
│   └── ClaimApprovalsController
│
├── ClaimWorkflowBackgroundService
│   └── starts scoped MAF executions
│       ├── ClaimIntakeAgentExecutor → IClaimIntakeAgent
│       ├── ClaimValidationExecutor → IClaimValidator
│       ├── RiskAssessmentAgentExecutor → IRiskAssessmentAgent
│       ├── HumanApprovalRequestExecutor → IHumanApprovalPolicy
│       ├── native RequestPort
│       └── ClaimDecisionExecutor
│
├── ApprovalExpiryService
│   └── periodic persisted-state maintenance
│
└── SignalR hub endpoint (mapped, but live page currently polls HTTP)

Browser
└── Blazor WebAssembly client
```

> We are not registering every agent as a `BackgroundService`.

The background worker starts MAF workflow executions; MAF invokes the graph's required executors, and those executors invoke agent abstractions or deterministic domain services.

## 23. Chapter 10 versus Chapter 1

```text
Chapter 10
Request → MAF workflow → executors → result
(one continuous execution period)

Chapter 1
Request → persist + queue → background MAF execution
        → native human request → persist + release execution
        → human decision later → queue
        → second MAF decision-continuation execution → result
(two execution periods sharing one logical WorkflowRunId)
```

Chapter 1 adds API/background decoupling, persisted run and approval state, expiry, reviewer APIs/UI, and a workflow that spans multiple execution periods. It no longer has to begin and finish in one continuous in-memory execution.

## Implementation notes and remaining limitations

1. **No native durable MAF checkpoint restoration.** `Microsoft.Agents.AI.Workflows` 1.15.0 does not expose a serializable `StreamingRun` checkpoint through the APIs used here. The sample disposes the original run and later runs `CreateDecisionContinuation()` with persisted input. It does not call `Request.CreateResponse`/`SendResponseAsync` after suspension and does not restore the exact native request-port runtime.
2. **Application-level continuation.** Completed intake/validation/risk nodes do not rerun, but this guarantee comes from a separate graph containing only `ClaimDecisionExecutor`, not from restoration of MAF's internal execution position.
3. **Approval policy is not a branch.** Every valid claim currently reaches the request port even when `HumanApprovalPolicy.IsRequired` is false. A true automatic path needs conditional routing or a distinct typed branch in the MAF graph.
4. **The channel is not durable.** Decided waiting approvals are recovered at startup, but queued starts are not. A crash can strand a `Queued` run. A crash after `ResumeAsync` changes a run to `Running` but before completion can also strand it because startup recovery scans only `WaitingForApproval`.
5. **Resume failure handling is incomplete.** `RunResumeAsync` does not catch, log, and persist continuation failures the way `RunStartAsync` does.
6. **Atomicity gaps remain.** Database writes and channel writes are not one transaction. The startup recovery and idempotency guards cover some, not all, crash windows; a production system needs an outbox/durable broker and explicit leases/retries.
7. **Serial processing and unbounded buffering.** The channel has one reader and is unbounded. That is simple for a teaching sample but is not throughput/backpressure design for production.
8. **SignalR is not the active live transport.** A hub is mapped, but the client currently polls workflow/event APIs. Polling is observation only and does not retain the workflow.
9. **Expiry is coarse-grained.** The timer runs every 30 seconds, so records may remain pending briefly after `ExpiresAt`; decision validation still checks the timestamp directly.
10. **Some diagnostic tables are presently unused.** The model contains agent-execution and message-transition tables, but the current executor implementation writes workflow events and executor traces, not those two tables.

These limitations are stated explicitly so the diagrams describe the code that runs today rather than implying native checkpoint capabilities or delivery guarantees that the sample does not have.
