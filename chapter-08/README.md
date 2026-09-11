# Chapter 8 — Agent-to-Agent (A2A)

This chapter demonstrates a distributed agent boundary: `TravelCoordinator → A2A over HTTP → DestinationExpert`. Each service is an independently hosted ASP.NET Core application with its own MAF `AIAgent`; neither application references the other project.

## Run

Configure the same `AzureOpenAI` settings in each project (endpoint, API key, and deployment), then run in separate terminals:

```powershell
dotnet run --project src/DestinationExpert.Api
dotnet run --project src/TravelCoordinator.Api
```

Open `https://localhost:60061`. The development profiles expose DestinationExpert at HTTPS port 60060 and HTTP port 60062, and TravelCoordinator at HTTPS port 60061 and HTTP port 60063. Server-to-server A2A traffic uses DestinationExpert's HTTP listener: its Agent Card is at `http://localhost:60062/.well-known/agent-card.json` and its MAF-hosted A2A endpoint is `/a2a/destination-expert`.

TravelCoordinator uses `A2ACardResolver` for discovery, converts the discovered card to a remote MAF `AIAgent` with `AsAIAgent`, and invokes it with `RunAsync`. The returned advice is passed into the coordinator's own MAF agent for synthesis. A remote `AgentSession` is retained for each browser conversation ID; the MAF A2A adapter maps that session to protocol context/task identifiers. No custom session protocol is introduced.

If DestinationExpert is stopped, the coordinator returns a controlled `503` result with a diagnostic event and remains healthy.

## Boundaries

- MCP connects an agent to a tool or capability.
- MAF multi-agent orchestration coordinates participating agents inside an orchestration.
- A2A connects independently hosted agent services through protocol messages across a network boundary.

`DemoMode=true` is used only by automated tests to substitute deterministic MAF agents for Azure OpenAI. The same official A2A server, discovery, client, network, and session paths remain active.
