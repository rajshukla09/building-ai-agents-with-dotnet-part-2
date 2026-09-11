# Chapter 7 code flow

```text
HTTP Request
   ↓
MAF Workflow Run
   ↓
ValidateRelease executor span
   ↓
Conditional message / edge group
   ├── HighRisk: yes → ExtraReview
   └── HighRisk: no  → ContinueReview
   ↓
Fan-out edge group
   ├── SecurityReview
   ├── QualityReview
   └── ArchitectureReview
   ↓
Fan-in barrier (buffers until all three sources emit)
   ↓
ReleaseDecision
```

The ASP.NET Core request activity is the parent of the MAF workflow session/run. The response uses that native trace ID as `runId` when available and also supplies it as the MAF session ID, so the HTTP request and workflow can be found together.

### What Native MAF Observability Gives Us

`WithOpenTelemetry` emits workflow build/session/run spans, `executor.process` spans, `edge_group.process` spans, and `message.send` spans. Those spans expose routing and delivery outcomes, executor failures and exception details, and the fan-in barrier's buffering/delivery behavior. The sample does not recreate these spans.

### Logs vs Metrics vs Traces

```text
Logs    = detailed events
Metrics = aggregated measurements
Traces  = end-to-end execution path
```

### Production measurements

Useful measurements include workflow, executor, and branch duration; fan-in wait time; workflow failures; dropped or conditionally routed messages; and, when agent integrations emit them, model/agent latency, tool/MCP latency, and token usage. This chapter concentrates on workflow traces rather than implementing a separate metrics layer.

### Sensitive data

`WorkflowTelemetryOptions.EnableSensitiveData` is `false` by default, excluding raw message inputs and executor outputs. For intentional local diagnostics only, set `WorkflowTelemetry__EnableSensitiveData=true`; do not use that setting with secrets or production payloads.

### OTLP viewer

```text
Application UI
    ↓
business-friendly workflow status

OpenTelemetry / OTLP
    ↓
native MAF distributed trace
```

The API exports to the standard `OTEL_EXPORTER_OTLP_ENDPOINT`; its development profile points to the standalone Aspire Dashboard on `http://localhost:4317`. The Blazor link opens the separately configured `TraceViewerUrl`. The trace/run ID displayed by the UI matches the W3C trace ID exported to the dashboard. No viewer, span copy, or telemetry database is embedded in the application.

For simulated failure, MAF 1.17.0 emits the `SecurityReview` executor span and no `ReleaseDecision` executor span. The native activity currently has an unset status and no exception event. The application does not manufacture replacement telemetry; the simplified UI reports the deterministic safe error message separately.
