# Verified Writing Context — Part 2, Chapter 8

## Agent-to-Agent (A2A)

Verified against `chapter-08` and its one passing integration test.

## What this chapter should teach

Teach communication between independently hosted Agents: publish an Agent Card, host an Agent with the official A2A ASP.NET Core adapter, discover a remote Agent, convert its card into a client-side MAF `AIAgent`, maintain remote context with `AgentSession`, pass remote output to a local coordinator Agent, and handle remote outage.

## What it adds over Chapter 7

Earlier multi-Agent examples run inside one application/runtime. Chapter 8 crosses a service and network boundary. New concepts are independent Agent hosts, Agent Card discovery, A2A HTTP/JSON, a remote Agent proxy, protocol-backed sessions, and remote/local response separation.

## Actual end-to-end flow

```text
Browser → POST /api/travel
  → CoordinatorService.RunAsync
  → A2ACardResolver.GetAgentCardAsync
  → GET /.well-known/agent-card.json
  → choose first SupportedInterface
  → AgentCard.AsAIAgent(HttpClient)
  → create/cache AgentSession by browser conversation ID
  → remoteAgent.RunAsync(message, session)
  → A2A HTTP/JSON endpoint
  → DestinationExpert AIAgent
  → remote advice returned
  → local TravelCoordinator AIAgent.RunAsync
  → synthesize user request plus labeled remote advice
  → return card, remote response, final response, timings, events
```

## Important code and APIs

- `DestinationExpert.Api/Program.cs`: Agent creation, Agent Card, A2A hosting, health, diagnostics.
- `TravelCoordinator.Api/Program.cs`: card discovery, remote proxy, session cache, remote call, local synthesis, errors.
- `A2AIntegrationTests.cs`: two real independent Kestrel hosts and network exchange.
- `wwwroot/app.js`: browser projection of card, remote response, final response, and events.

APIs: `AddA2AServer`, `MapWellKnownAgentCard`, `MapA2AHttpJson`, `A2ACardResolver`, `AgentCard.AsAIAgent`, `AIAgent.CreateSessionAsync`, and session-aware `RunAsync`.

The A2A packages are preview `1.17.0-preview.260804.1`.

## Model-driven versus deterministic

Production model-driven behavior is remote destination advice and local synthesis.

Deterministic behavior is the configured remote base address, card discovery, first-interface selection, remote-then-local ordering, conversation lookup, event creation, and HTTP 503 mapping. No model chooses among remote Agents.

## Persistence and runtime

Remote sessions are cached in an in-memory `ConcurrentDictionary` under caller-provided conversation IDs. They do not survive coordinator restart. There is no database, durable message queue, retry scheduler, or recovery. Card discovery occurs on every request. Sessions are not expired or removed. The remote invocation counter is process-local diagnostics.

## Tests: exactly what they prove

The integration test proves two independent Kestrel services, health endpoints, card discovery over HTTP, correct card metadata, official A2A endpoint invocation, increased remote invocation count, delivery of remote advice to local synthesis, reuse of a conversation ID in DemoMode, HTTP 503 after remote shutdown, and continued coordinator health.

It does not prove Azure OpenAI, authentication, persistent sessions, multiple remote selection, streaming, distributed tracing, or production retries/timeouts.

## Limitations

- Preview packages and non-streaming Agent Card.
- No authentication, authorization, identity propagation, tenant isolation, or production TLS policy.
- Caller-controlled conversation IDs are not secure session identifiers.
- Unbounded, process-local session cache.
- Concurrent creation can construct an unused extra session before `GetOrAdd` resolves the winner.
- First advertised interface is used without explicit binding/version validation.
- Discovery, remote call, and local synthesis failures share a broad 503 response.
- Timeline events are application diagnostics, not protocol traces; duration meanings are mixed.
- Empty messages are not rejected.
- Exactly one configured remote service is supported.

## Claims to avoid

Do not claim the model chooses a remote Agent; A2A is an orchestration manager; Agent Cards are MCP catalogs; sessions survive restart; conversation IDs are authenticated; streaming is supported; the test uses Azure OpenAI; A2A messages are durable; failure stages are precisely distinguished; or distributed tracing is included.

## Recommended sections

1. From in-process Agents to independent services
2. A2A versus MCP and orchestration
3. Hosting DestinationExpert
4. Publishing the Agent Card
5. Discovering the remote Agent
6. Adapting the card to a MAF `AIAgent`
7. Sending an A2A request
8. Maintaining remote conversation context
9. Synthesizing remote advice locally
10. Showing boundaries in the UI
11. Handling remote outage
12. Tests, preview APIs, security, and durability

## Reference instead of repeating

Reference Chapter 2 for MCP and explain only the protocol-role difference. Reference Chapters 3–5 for in-process orchestration. Reference Chapter 7 when stating that cross-service trace propagation is not implemented.
