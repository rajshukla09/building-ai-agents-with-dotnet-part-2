# Chapter 7 Technical Writing Notes — OpenTelemetry for Workflows

## Chapter Purpose
Shows native MAF Workflow tracing with OpenTelemetry and OTLP export. Readers learn to correlate application results with framework spans without fabricating telemetry.

## Application / Use Case
A release-review workflow validates a request, chooses normal/high-risk routing, fans out Security/Quality/Architecture reviews, fans in to a release decision, and optionally demonstrates failure.

## Architecture
`Blazor → API → ReleaseReviewWorkflow → native Executors/Workflow → response`
`ASP.NET Core + MAF ActivitySource → OpenTelemetry → OTLP → Aspire Dashboard`

## End-to-End Execution Flow
1. UI posts release text and optional failure flag.
2. `ValidateRelease` creates typed validated input.
3. deterministic routing includes `ExtraReview` only for high risk.
4. required review Executors run concurrently and emit findings.
5. `ReleaseDecision` runs after fan-in unless a required branch fails.
6. Workflow events become an API response with Executor status/timing and trace ID.
7. Native spans export through configured OTLP endpoint and can be opened in the dashboard.

## Important Code
- `ReleaseReviewWorkflow` builds topology, calls `InProcessExecution.Concurrent.RunAsync`, captures events, and enables native OpenTelemetry.
- typed Executor classes own validation, routing, reviews, and final decision.
- `ServiceCollectionExtensions` configures resource, ASP.NET Core instrumentation, source subscription, and OTLP exporter.
- UI workflow/timeline/failure/fan-out components map result fields to technical stages.

## Important MAF APIs
`WorkflowBuilder`, typed `Executor<TIn,TOut>`, `InProcessExecution.Concurrent.RunAsync`, and `.WithOpenTelemetry` produce native Workflow execution and spans. OpenTelemetry hosting/exporter APIs send them over OTLP.

## Data and State Flow
Typed review messages cross workflow edges. The API response contains status, decision, Executor records, route, timing, and trace ID. Telemetry exports span attributes/events; no business persistence is used.

## Agent Decision Points
No LLM Agents are used: validation, high-risk routing, findings, and decision are deterministic simulated Executors so telemetry shape is reproducible.

## Guardrails and Validation
Typed edges, deterministic routing, cancellation, safe failure messages, endpoint validation, configured OTLP endpoint requirement, and sensitive-data guidance bound the sample.

## Persistence and Recovery
No application persistence or durable continuation. The external trace backend retains telemetry according to its own configuration.

## Observability
UI shows route, Executor timeline, durations, fan-out/fan-in, skipped/not-executed nodes, trace ID, and error. Aspire Dashboard shows distributed trace spans emitted by ASP.NET Core and MAF.

## UI Flow
Request panel starts the workflow; route callout maps risk decision; diagram maps graph execution; timeline maps returned Executor records; trace link uses the correlation ID; failure panel explains the stopped branch.

## Demo / Test Scenario
Run normal, high-risk, and simulated-failure reviews. Observe ExtraReview skipped/completed and ReleaseDecision not executed after required failure; open the corresponding trace.

## Failure / Edge Cases
Missing OTLP configuration prevents correct startup, simulated Security failure stops fan-in, cancellation stops execution, and exporter unavailability affects telemetry delivery rather than business logic.

## Key Teaching Points
- Prefer native framework spans to application-invented replacements.
- A trace backend and an application status UI answer different questions.

## Common Misunderstandings
Deterministic simulated Executors are not Agents. OpenTelemetry is observability, not workflow persistence or recovery.

## What This Chapter Does Not Cover
LLM quality telemetry, production sampling/retention/alerts, durable replay, MCP, or distributed downstream services.

## Connection to Other Chapters
Uses workflow ideas from Chapters 10/15. Chapter 8 demonstrates a genuinely distributed Agent boundary where trace propagation would be a next concern.
