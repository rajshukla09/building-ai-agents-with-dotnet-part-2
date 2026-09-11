# Chapter 8 Technical Writing Notes — Agent-to-Agent (A2A)

## Chapter Purpose
Introduces real communication between independently hosted Agents using A2A. Readers learn how this differs from in-process multi-agent orchestration and MCP tool invocation.

## Application / Use Case
A Travel Coordinator service asks a separately hosted Destination Expert Agent for destination advice, then its local coordinator Agent synthesizes a final recommendation. Demo mode keeps model output deterministic while retaining the real A2A boundary.

## Architecture
`Browser → TravelCoordinator.Api → A2A client Agent → HTTP/A2A → DestinationExpert.Api + Agent Card → remote Agent → A2A response → local coordinator AIAgent → result`

## End-to-End Execution Flow
1. Destination Expert starts independently, creates its `AIAgent`, maps A2A endpoints, Agent Card, health, and diagnostics.
2. Browser discovery requests the remote Agent Card through the coordinator.
3. `CoordinatorService` fetches the card and adapts it with `card.AsAIAgent(http)`.
4. It creates/caches a remote `AgentSession` and calls the remote Agent through A2A.
5. Destination Expert executes and returns advice over the A2A protocol.
6. Local coordinator `AIAgent.RunAsync` receives user request plus explicitly labeled remote advice and synthesizes the final answer.
7. API returns remote response, final response, timings, Agent Card data, and boundary events; UI renders them.

## Important Code
- `DestinationExpert.Api/Program.cs` creates the remote Agent, hosts A2A, publishes health/diagnostics, and supplies demo/real model configuration.
- `TravelCoordinator.Api/Program.cs` configures HttpClient, local Agent, API/static UI, and `CoordinatorService`.
- `CoordinatorService.RunAsync` owns card discovery, remote Agent adaptation/session, A2A invocation, local synthesis, timing, and safe errors.
- `wwwroot/app.js` maps discovery/run calls to Agent Card, event, remote-response, and final-result panels.
- integration tests host both services and verify the remote invocation counter.

## Important MAF APIs
`AsAIAgent` creates local/remote model Agents; `AIAgent.CreateSessionAsync` and `RunAsync` maintain the remote Conversation; A2A hosting maps the Destination Expert, and Agent Card `AsAIAgent(HttpClient)` creates an A2A client-side Agent proxy.

## Data and State Flow
`TravelRequest` crosses the coordinator API. An A2A message crosses HTTP to the remote Agent and returns remote text. Local synthesis receives that text. `TravelResult` contains both responses and event/timing metadata.

## Agent Decision Points
Destination Expert model chooses advice; coordinator model chooses synthesis. C# deterministically discovers the Agent Card, selects the configured remote Agent, manages sessions, orders calls, and labels boundaries.

## Guardrails and Validation
Request validation, configured remote base URL, HTTP cancellation, health/discovery failures, safe exception mapping, explicit remote-advice labeling, and independently testable invocation count bound the sample.

## Persistence and Recovery
Remote Conversation sessions are held in coordinator memory and do not survive restart. No business database or durable A2A queue is included.

## Observability
UI and response show discovery, remote invocation, local synthesis, durations, Agent Card capabilities, and failures. Destination Expert diagnostics expose invocation count for integration verification.

## UI Flow
Discover Agent loads the Agent Card; Send to Coordinator starts the distributed path; boundary graphic identifies hosted services; event list shows crossings; separate panels prevent confusing remote advice with final local synthesis.

## Demo / Test Scenario
In DemoMode, ask “Should I visit Kyoto in spring?” Observe successful Agent Card discovery, one remote invocation, remote advice, then coordinator synthesis. Integration tests assert the remote service was actually called.

## Failure / Edge Cases
Remote service unavailable, invalid Agent Card, A2A call failure, cancellation, or local synthesis failure are surfaced separately.

## Key Teaching Points
- A2A is independently hosted Agent-to-Agent communication.
- Agent Card discovery describes the remote Agent; it is not an MCP Tool catalog.

## Common Misunderstandings
A2A is not MCP: MCP connects an Agent to a capability, while A2A connects independently hosted Agents. The coordinator's two calls are application sequencing, not Magentic.

## What This Chapter Does Not Cover
Authentication/authorization, durable messaging, service discovery platform, multi-peer routing, cross-service OpenTelemetry propagation, or production travel data.

## Connection to Other Chapters
Builds on `AIAgent`/Agent Session concepts and contrasts with Chapter 3's in-process Supervisor/Specialist Agents. It is the repository's distributed Agent boundary.
