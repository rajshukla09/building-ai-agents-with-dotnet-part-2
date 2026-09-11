# Chapter 6 — Durable Purchase Approval

This Swagger-only sample runs the complete Microsoft Agent Framework workflow through the Durable Task extension. Validation and budget checking execute once, the native request port becomes a durable external-event wait, and the same orchestration instance resumes with procurement after the API/worker restarts.

SQLite is limited to purchase audit/business data and the idempotent procurement record. Durable Task Scheduler owns workflow state, checkpoints, pending approval, status, and resumption.

## Run

Start a Durable Task Scheduler compatible with the configured Azure Managed Durable Task client and worker. The development default is:

```text
Endpoint=http://localhost:8080;TaskHub=default;Authentication=None
```

Override `ConnectionStrings:DurableTaskScheduler` when necessary, then run:

```bash
dotnet run --project src/PurchaseApproval.Api
```

Swagger is `/swagger`. Endpoints are `POST /api/purchases`, `GET /api/purchases/{instanceId}`, `POST /api/purchases/{instanceId}/approve`, and `POST /api/purchases/{instanceId}/reject`.
