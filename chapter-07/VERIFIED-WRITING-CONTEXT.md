# Verified Writing Context — Part 2, Chapter 7

## OpenTelemetry for Workflows

Verified against `chapter-07` and its ten passing tests.

## What this chapter should teach

Teach native MAF workflow tracing: enable workflow OpenTelemetry, subscribe to the MAF activity source, export through OTLP, correlate HTTP and workflow spans, inspect conditional routing and fan-out/fan-in, control sensitive payload capture, and describe current native failure-span limitations accurately.

## What it adds over Chapter 6

Chapter 6 concerns execution survival; Chapter 7 concerns observing execution. New concepts include `WithOpenTelemetry`, the MAF `ActivitySource`, ASP.NET Core instrumentation, OTLP, Aspire Dashboard, trace correlation, and sensitive-data controls. Reference earlier chapters for conditional edges and fan-out/fan-in.

## Actual end-to-end flow

```text
POST /api/releases/review
  → ASP.NET Core request Activity
  → runId uses current TraceId
  → ValidateRelease
  → deterministic high-risk conditional edge
  → ExtraReview or ContinueReview
  → fan-out to Security, Quality, Architecture
  → fan-in barrier to ReleaseDecision
  → native MAF activities emitted
  → OpenTelemetry exports through OTLP
  → Aspire Dashboard displays the trace
  → API returns business response, not telemetry payloads
```

The workflow is fully deterministic and contains no `AIAgent`.

## Important code and APIs

- `ServiceCollectionExtensions.cs`: resource, ASP.NET instrumentation, MAF source, OTLP exporter.
- `ReleaseReviewWorkflow.cs`: topology, run/trace correlation, `WithOpenTelemetry`, response behavior.
- `Executors/*`: validation, branches, fan-out reviews, aggregation, and simulated failure.
- `NativeTelemetryTests.cs`: direct native `Activity` assertions.
- `compose.yaml`: local Aspire Dashboard and OTLP endpoints.

APIs: `WorkflowTelemetryOptions`, `WithOpenTelemetry`, `InProcessExecution.Concurrent.RunAsync`, `AddSource("Microsoft.Agents.AI.Workflows")`, `AddAspNetCoreInstrumentation`, `AddOtlpExporter`, and `Activity.Current.TraceId`.

## Model-driven versus deterministic

There is no model behavior. High-risk keyword classification, branch choice, three reviews, simulated failure, aggregation, and response projection are deterministic so trace shape is reproducible.

## Persistence and runtime

The application has no business or trace database. OTLP sends traces to an external collector; the included dashboard is ephemeral unless separately configured. Under an HTTP Activity, the response run ID is the HTTP trace ID and is also supplied as the MAF session ID. The API does not query or return collector spans; the UI only links to the dashboard.

## Tests: exactly what they prove

Tests prove normal/high-risk/failure business outcomes, sensitive-data option binding, endpoint responses, native MAF workflow/executor/edge/message activities sharing a parent trace ID, absence of tested sensitive text in tags, and a failure trace that reaches `SecurityReview` without `ReleaseDecision`.

They do not prove OTLP network export, Aspire ingestion/rendering, logs, metrics, retention, or cross-service propagation.

## Limitations

- Traces only; not a complete logs/metrics stack.
- `BuildExecutions` creates a simplified expected list rather than reading spans.
- Failure projection may report quality/architecture as completed without inspecting actual scheduling.
- All exceptions, including cancellation, become failed business responses.
- Empty release input returns HTTP 200/Failed rather than the advertised 400.
- `ReleaseDecision` keeps per-run bags in a singleton dictionary without removal.
- In MAF `1.17.0`, failed executor Activity status may remain `Unset` without exception events.
- Sensitive-data tests do not inspect every exporter representation.
- `ExtraReview` is a pass-through node, not an AI or human review.

## Claims to avoid

Do not claim Agents or Azure OpenAI are used; responses contain telemetry; the application persists traces; the UI queries traces; OTLP is integration-tested; executor response statuses come from spans; current failure spans contain exception text/error status; cross-service propagation exists; or cancellation propagates unchanged.

## Recommended sections

1. Why workflows need traces
2. The deterministic release-review graph
3. Enabling native MAF instrumentation
4. Registering the MAF activity source
5. HTTP/workflow correlation
6. Executor, edge, message, and fan-out spans
7. OTLP export
8. Aspire Dashboard inspection
9. Sensitive-data controls
10. The simulated failure trace
11. Business UI versus trace viewer
12. Tests and native telemetry limitations

## Reference instead of repeating

Reference Chapter 1 for conditional edges and Chapter 5 for fan-out/fan-in. This chapter is the canonical explanation of native MAF OpenTelemetry; Chapter 8 should explicitly note that cross-service propagation is absent.
