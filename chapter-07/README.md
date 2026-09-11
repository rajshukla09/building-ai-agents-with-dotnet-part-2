# Chapter 7 — OpenTelemetry for Workflows

Chapter 7 demonstrates native Microsoft Agent Framework 1.17.0 workflow tracing with OpenTelemetry, OTLP export, and the standalone Aspire Dashboard.

Run `dotnet run --project src/ObservableReleaseReview.Api`, open `/swagger`, and call `POST /api/releases/review`:

```json
{
  "release": "Release 4.8 contains authentication and payment changes.",
  "simulateFailure": false
}
```

The response contains `runId`, workflow `status`, `finalDecision`, and `durationMs`; it never returns telemetry payloads. Set `OTEL_EXPORTER_OTLP_ENDPOINT` to your collector endpoint. See [CODE-FLOW.md](CODE-FLOW.md) for span behavior and data-safety settings.

## Run the complete sample locally

### 1. Start the trace viewer

From `chapter-07`, start the standalone Aspire Dashboard:

```powershell
docker compose up -d
```

This exposes the dashboard at `http://localhost:18888`, OTLP/gRPC at `http://localhost:4317`, and OTLP/HTTP at `http://localhost:4318`. Anonymous access is enabled only for this local teaching setup. No telemetry is persisted when the container stops.

The equivalent one-line command is:

```powershell
docker run --rm -d --name chapter18-aspire-dashboard -p 18888:18888 -p 4317:18889 -p 4318:18890 -e ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true mcr.microsoft.com/dotnet/aspire-dashboard:latest
```

### 2. Start the API and UI

Start the API and Blazor WebAssembly projects in separate terminals:

```powershell
dotnet run --project src/ObservableReleaseReview.Api
dotnet run --project src/ObservableReleaseReview.Web
```

The launch profiles use `https://localhost:51423` for the API and `https://localhost:7280` for the UI. The API profile sets `OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317`; override that standard variable for another collector. Configure `ApiBaseAddress` and `TraceViewerUrl` in the web project's `wwwroot/appsettings.json` when using different addresses.

### 3. Run and find a trace

1. Open `https://localhost:7280` and run a normal, high-risk, or simulated-failure review.
2. Copy the trace/run ID shown in Run Summary or Telemetry Details.
3. Expand **Telemetry Details** and select **Open distributed trace**.
4. In Aspire Dashboard, open **Traces** and locate the trace with the matching trace ID. Aspire Dashboard does not currently provide a stable trace-ID deep-link contract, so the button opens the configured dashboard rather than constructing a fragile URL.
5. Expand the trace to inspect the ASP.NET Core request, native MAF workflow/session activities, executor spans, edge-group routing, message delivery, fan-out/fan-in, and exception details.

The response run ID is `Activity.Current.TraceId` from the HTTP request and the same value is supplied as the MAF session ID. There is no second correlation identifier.

For the failure example, use:

```json
{
  "release": "Release 4.8 contains authentication and payment changes.",
  "simulateFailure": true
}
```

The business UI shows the simplified failure path. In the installed MAF 1.17.0 package, the native trace reaches `executor.process SecurityReview` and has no downstream `ReleaseDecision` span. This package version currently leaves the failed executor activity status as `Unset` and does not attach an exception event, so the Aspire trace identifies the failure boundary but does not display exception text. The sample deliberately does not add a competing custom error span to conceal that native limitation.

### Sensitive data

Raw message inputs and executor outputs remain excluded because `WorkflowTelemetryOptions.EnableSensitiveData` defaults to `false` in `appsettings.json`. For intentional local-only diagnostics, set `WorkflowTelemetry__EnableSensitiveData=true` before starting the API. Never enable it for secrets or production payloads.

Stop the local viewer with `docker compose down`.
