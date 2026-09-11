# Chapter 2 Technical Writing Notes — Enterprise Knowledge Assistant using MCP

## Chapter Purpose
Introduces a real MCP protocol/transport boundary and distinguishes capability discovery from Agent selection. Readers learn that MCP connects an Agent to tools; it does not orchestrate multiple Agents.

## Application / Use Case
An enterprise assistant answers questions using read-only GitHub, CRM, documentation, and SQL-style capabilities. The returned data is deterministic sample data behind a real MCP server process.

## Architecture
`Caller → AssistantController → EnterpriseAssistantAgent → MafEnterpriseModelRunner/AIAgent → MCP tool function → EnterpriseMcpClient → stdio → EnterpriseMcpTools server → sample data`

## End-to-End Execution Flow
1. Normal startup registers `EnterpriseMcpClient` and assistant services.
2. On first discovery, the client starts the same assembly with `--mcp-server`.
3. Server mode calls `AddMcpServer().WithStdioServerTransport().WithTools<EnterpriseMcpTools>()`.
4. `McpClient.ListToolsAsync` discovers capabilities; the client filters to read-only annotations.
5. `EnterpriseAssistantAgent` exposes the request catalog to the MAF Agent.
6. The model selects zero or more MCP Tools within the configured budget.
7. `EnterpriseMcpClient.InvokeAsync` validates server, discovery membership, and arguments before `CallAsync` crosses stdio.
8. Tool results return to the Agent for final synthesis; invocation telemetry is returned to the caller.

## Important Code
- `Program.cs` owns dual web/MCP-server startup and DI.
- `EnterpriseMcpClient` owns process transport, discovery, schema validation, invocation, logging, and disposal.
- `EnterpriseMcpTools` exposes annotated read-only MCP Tools and deterministic sample results.
- `EnterpriseAssistantAgent` owns request policy/budget and telemetry.
- `MafEnterpriseModelRunner` adapts discovered tools to an `AIAgent` invocation.
- `McpController` exposes server/tool discovery; `AssistantController` exposes questions.

## Important MAF APIs
`AsAIAgent`, `AIAgent`, and `RunAsync` perform model selection/synthesis. MCP APIs include `McpClient`, `StdioClientTransport`, `ListToolsAsync`, `CallAsync`, `AddMcpServer`, `WithStdioServerTransport`, and MCP Tool annotations.

## Data and State Flow
Discovered schemas form a per-request catalog. Model arguments are JSON, validated against that catalog, and cross stdio. Structured/text MCP content returns as `JsonElement`. Invocation records contain safe metadata.

## Agent Decision Points
The model decides which discovered MCP Tool to call and how to synthesize results. C# filters read-only tools, validates arguments, enforces budget, and controls process connection.

## Guardrails and Validation
Read-only annotation filtering, discovered-catalog allow-listing, schema validation, maximum tool calls, cancellation, and safe errors bound calls.

## Persistence and Recovery
No business persistence. The MCP client connection is process-local and recreated after restart.

## Observability
Logs show connection, discovery, validated calls, results, and failures; response telemetry shows selected tools and outcomes; Swagger exposes discovery endpoints.

## UI Flow
Swagger lists MCP servers/tools and submits assistant questions. It demonstrates capability discovery separately from Agent invocation.

## Demo / Test Scenario
Ask for a project/customer status requiring documentation and CRM evidence. Observe discovery, model-selected MCP calls, and a synthesized answer.

## Failure / Edge Cases
Server startup failure, unknown server/tool, missing/extra/wrong-type arguments, tool error, budget exhaustion, and cancellation are explicit.

## Key Teaching Points
- MCP is Agent-to-capability communication through a protocol boundary.
- Real MCP transport can sit in front of demo data; that does not make the data source production-real.

## Common Misunderstandings
MCP does not coordinate Agents or create workflow state. The stdio server is real MCP; GitHub/CRM/SQL results are samples.

## What This Chapter Does Not Cover
Authentication, network-hosted MCP, production enterprise integrations, multi-agent supervision, or durable state.

## Connection to Other Chapters
Contrasts with Chapter 6 local functions. Chapter 3 adds Supervisor-selected Specialist Agents that independently use real MCP.
