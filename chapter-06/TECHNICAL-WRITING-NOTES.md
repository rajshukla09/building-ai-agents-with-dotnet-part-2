# Chapter 6 Technical Writing Notes — Durable Purchase Approval

## Chapter Purpose
Introduces native Durable Workflow hosting and restart-safe Human-in-the-Loop continuation. Readers learn the distinction between Durable Task orchestration state and business records.

## Application / Use Case
A purchase request is validated, checked against budget, optionally waits for manager approval, and proceeds to procurement using a Durable Task Scheduler.

## Architecture
`Caller → PurchaseService → MAF Durable Workflow/DurableTaskClient → ValidatePurchase → BudgetCheck → request-port approval → Procurement`
`Executors ↔ SQLite business records; Durable Task Scheduler ↔ orchestration state`

## End-to-End Execution Flow
1. POST creates a purchase business record and stable instance ID.
2. `PurchaseService` starts `PurchaseWorkflow.Definition` through the durable workflow client.
3. `ValidatePurchase` deterministically validates and records state.
4. `BudgetCheck` determines whether approval is required.
5. The Workflow either continues or waits at `AwaitManagerApproval` request port.
6. Approval endpoint validates current state and raises the external decision to the durable instance.
7. `Procurement` records Approved/Rejected completion.
8. Status endpoint combines business state with durable progress.

## Important Code
- `PurchaseWorkflow` declares typed durable Executors, edges, and request-port path.
- `PurchaseService` starts instances, queries state, and sends manager decisions.
- `PurchasesDb` stores business/audit records; comments explicitly separate it from Durable Task state.
- `Program.cs` configures `ConfigureDurableWorkflows`, worker/client scheduler providers, DI, and minimal API endpoints.

## Important MAF APIs
`WorkflowBuilder`, typed `Executor<TIn,TOut>`, request ports, `ConfigureDurableWorkflows`, and durable workflow `RunAsync` integrate MAF with `DurableTaskClient` and Azure Managed Durable Task Scheduler.

## Data and State Flow
`StartMessage`, `ValidatedMessage`, `BudgetMessage`, `ApprovalRequest`, `ApprovalDecision`, and `CompletedMessage` cross typed workflow boundaries. Durable Task stores orchestration/checkpoint state; SQLite stores purchase business status and audit timestamps.

## Agent Decision Points
There is no LLM decision in the implemented workflow. Validation, budget threshold, waiting, and procurement are deterministic C#; the human supplies the approval decision.

## Guardrails and Validation
Request validation, stable IDs, state preconditions, idempotent decision behavior, cancellation tokens, and Durable Task replay constraints protect execution.

## Persistence and Recovery
Durable Task Scheduler checkpoints workflow state and can continue after host restart. SQLite survives business queries. Neither store substitutes for the other.

## Observability
Application logs identify instance start/decision; API status exposes business state; scheduler tooling exposes durable orchestration state.

## UI Flow
No bespoke UI; minimal API/Swagger demonstrates create, status, approve/reject, and cancellation operations.

## Demo / Test Scenario
Submit a purchase above the approval threshold, observe waiting state, restart the host if desired, approve it, and observe procurement completion. Tests cover low/high-value and decision behavior.

## Failure / Edge Cases
Missing scheduler, invalid purchase, unknown instance, duplicate/late decision, rejection, and restart while waiting are meaningful paths.

## Key Teaching Points
- Durable Workflow state is infrastructure orchestration state; business state still belongs in an application store.
- Durable replay favors deterministic Executor code.

## Common Misunderstandings
“Durable” does not mean SQLite alone. Human-in-the-Loop waiting is not an Agent decision, and this sample contains no MCP.

## What This Chapter Does Not Cover
LLM Agents, a browser UI, production identity/authorization, scheduler provisioning, or compensation across external procurement systems.

## Connection to Other Chapters
Makes Chapter 1's restart boundary native and durable. Chapter 7 focuses on observability rather than durability.
