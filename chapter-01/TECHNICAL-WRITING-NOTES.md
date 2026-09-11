# Chapter 1 Technical Writing Notes — Human-in-the-Loop Workflows

## Chapter Purpose
Introduces conditional human approval in a claims workflow. Readers learn where deterministic policy should suspend automated processing and how application persistence supports later continuation.

## Application / Use Case
A user submits an insurance claim. Intake and risk Agents analyze it; deterministic policy either completes automatically or creates a pending approval for a reviewer, who approves or rejects through the Blazor UI.

## Architecture
`Blazor → API → queue/worker → MAF Workflow → intake Agent → validation Executor → risk Agent → approval policy/request port → SQLite`
`Reviewer → approval API → persisted-data-driven continuation → final decision`

## End-to-End Execution Flow
1. Submission creates a persisted run and queues work.
2. Intake Agent creates a structured summary.
3. Validation deterministically checks required claim data.
4. Risk Agent produces structured risk assessment.
5. Policy deterministically decides whether human approval is required.
6. Low-risk claims continue automatically; qualifying claims persist `Pending` approval and enter `WaitingForApproval`.
7. A reviewer decision is validated/idempotently stored.
8. A continuation workflow reads persisted data and completes Approved or Rejected outcome.
9. SignalR/polling updates the UI.

## Important Code
- workflow Executors own intake, validation, risk, approval request, and decision stages.
- the background worker serially consumes queued workflow items outside the HTTP request.
- EF Core repositories own claim run, approval, event, and trace records.
- controllers expose submission, run status, approval list/detail, decision, cancellation, and expiry.
- `ClaimsReview.Client` maps reviewer actions and live workflow state to those endpoints.

## Important MAF APIs
Typed Executors and `WorkflowBuilder` define the flow. A request port represents the HITL boundary. Agent `RunAsync<T>` powers intake/risk. The installed workflow API does not expose a serializable live run checkpoint used here, so continuation is reconstructed from persisted application data.

## Data and State Flow
Claim DTOs become typed workflow messages. Agent results, deterministic status/events, approval record, and final decision are stored in SQLite. SignalR carries run-change notifications, not authoritative state.

## Agent Decision Points
Agents summarize and assess risk. C# validates claims, applies approval thresholds, accepts reviewer identity/decision, handles expiry/cancellation, and determines final business transitions.

## Guardrails and Validation
Structured validation, deterministic approval policy, one effective reviewer decision, status preconditions, expiry, cancellation, safe errors, and persisted audit events bound the process.

## Persistence and Recovery
SQLite survives restart. Pending approvals can be reopened and continuation can be started from persisted records. This is application-managed continuation, not Chapter 6 Durable Task checkpoint recovery.

## Observability
Executor traces, workflow events, approval status, durations, and errors are visible in API/UI; SignalR makes updates timely.

## UI Flow
Submit Claim creates a run; Live Workflow maps cards to stages; Pending Approvals shows request-port work; Approval Details posts the human decision; Approved Claims shows completed business outcomes.

## Demo / Test Scenario
Submit a high-value claim that meets approval policy, observe Waiting for Approval, approve it, and observe Completed. Also submit a low-risk claim and observe no human pause.

## Failure / Edge Cases
Validation failure, rejection, duplicate decisions, expiry, cancellation, restart while waiting, and background failure are handled explicitly.

## Key Teaching Points
- Human-in-the-Loop is a deterministic trust boundary around Agent output.
- Waiting is a valid workflow state, not a failure.

## Common Misunderstandings
Human approval is not Magentic planning. SQLite business/audit state is not native durable workflow state.

## What This Chapter Does Not Cover
Durable Task Scheduler, distributed workers, production identity/authorization, or MCP.

## Connection to Other Chapters
Builds on Chapter 10 workflow concepts. Chapter 6 implements native Durable Workflow continuation.
