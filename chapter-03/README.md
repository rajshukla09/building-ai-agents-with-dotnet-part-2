# Chapter 3 — Dynamic Multi-Agent Orchestration

`EnterpriseIncidentInvestigator` combines an ASP.NET Core API with a Blazor orchestration viewer. A Microsoft Agent Framework (MAF) Supervisor dynamically delegates production-incident investigation to Specialist Agents. Each Specialist Agent can discover and invoke focused tools on a dedicated MCP server process over stdio, without a fixed agent sequence.

The central rule is: **the supervisor decides WHO should work; a specialized agent decides HOW to investigate; MCP provides WHAT capabilities are available.** See [CODE-FLOW.md](CODE-FLOW.md) for the detailed execution flow, guardrails, tests, and MAF limitations.

## Configure and run

```bash
dotnet user-secrets --project src/EnterpriseIncidentInvestigator.Api set AzureOpenAI:Endpoint https://YOUR-RESOURCE.openai.azure.com/
dotnet user-secrets --project src/EnterpriseIncidentInvestigator.Api set AzureOpenAI:ApiKey YOUR-KEY
dotnet user-secrets --project src/EnterpriseIncidentInvestigator.Api set AzureOpenAI:DeploymentName gpt-4o-mini
dotnet run --project src/EnterpriseIncidentInvestigator.Api
# In a second terminal:
dotnet run --project src/EnterpriseIncidentInvestigator.Web
```

Open `/swagger` and call `POST /api/incidents/investigate`:

```json
{ "incident": "Checkout failures increased after this morning's deployment." }
```

Deterministic demo MCP data represents deployment v4.8 at 08:00, a failure-rate increase at 08:05, a healthy database, and a `NullReferenceException` in the new payment-token mapping. Different prompts can cause different model-selected routes, and meaningful repeated delegation is supported.

Open the Blazor orchestration viewer at `http://localhost:5131`. It sends a client-generated run ID, subscribes to that run through SignalR, and renders persisted SQLite events as they arrive. The viewer includes recent-run history, the actual supervisor-selected path, decision rationale, context summaries, agent findings, nested MCP calls and budgets, failures, and the final structured root cause. The UI observes MAF; it never selects or sequences agents.

The API also exposes `GET /api/incidents/runs`, `GET /api/incidents/runs/{runId}`, and `GET /api/incidents/runs/{runId}/events` for reconstructing persisted runs.

## Test

```bash
dotnet test EnterpriseIncidentInvestigator.sln
```
