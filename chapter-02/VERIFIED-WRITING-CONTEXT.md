# Verified Writing Context — Part 2, Chapter 2

## Enterprise Knowledge Assistant with MCP

Verified against the current `chapter-02` implementation and its seven passing tests. The current source—not older README or CODE-FLOW statements—is authoritative.

## What this chapter should teach

Introduce MCP from the capability-consumer perspective: host a real MCP server over stdio, discover its tools through the official client, convert the discovered capabilities into MAF functions, let an `AIAgent` choose and sequence calls, and keep authorization, schema checks, and call limits deterministic. The protocol boundary is real; the enterprise data is deterministic demonstration data.

## What it adds over Chapter 1

Chapter 1 used bounded Agents inside a workflow. This chapter gives one Agent runtime-discovered external capabilities. Reference Chapter 1 for `AIAgent`, Azure OpenAI configuration, cancellation, and the rule that model output does not grant authority.

## Actual end-to-end flow

```text
POST /api/assistant/query
  → EnterpriseAssistantAgent.QueryAsync
  → EnterpriseMcpClient.DiscoverToolsAsync
  → start current assembly with --mcp-server
  → MCP ListToolsAsync over stdio
  → retain ReadOnly tools
  → create request-scoped ModelTool delegates
  → MafEnterpriseModelRunner converts them with AIFunctionFactory.Create
  → AIAgent.RunAsync
  → model selects zero or more tools
  → InvocationLedger reserves a call
  → EnterpriseMcpClient validates and invokes McpClientTool.CallAsync
  → EnterpriseMcpTools returns deterministic data
  → result returns to the model
  → final answer and invocation telemetry return to the API
```

There is one MCP server, `enterprise-knowledge`, with three tools: `search_github`, `search_documentation`, and `find_customer`.

## Important code and APIs

- `Program.cs`: dual API/MCP-server mode; `AddMcpServer`, `WithStdioServerTransport`, `WithTools<EnterpriseMcpTools>`.
- `EnterpriseMcpClient.cs`: `McpClient.CreateAsync`, `ListToolsAsync`, read-only filtering, request-catalog checks, input validation, and `CallAsync`.
- `EnterpriseAssistantAgent.cs`: request catalog, tool wrappers, `InvocationLedger`, response telemetry.
- `MafEnterpriseModelRunner.cs`: `AIFunctionFactory.Create`, `ChatClientAgentOptions.ChatOptions.Tools`, `AsAIAgent`, `RunAsync`.
- `EnterpriseMcpTools.cs`: MCP annotations and deterministic tool bodies.

## Model-driven versus deterministic

Model-driven: whether to call a tool, which discovered tool to call, the query argument, whether to call another tool, and final synthesis.

Deterministic: configured server, exposed tools, read-only filtering, catalog membership, required argument and string-type checks, maximum calls, invocation sequencing, and demo results.

## Persistence and runtime

There is no business persistence. The singleton MCP client and child process are reused in memory. Restart loses the connection and catalog but no durable application state. Tool arguments, results, and errors are logged and are also included in response telemetry.

## Tests: exactly what they prove

The seven tests prove real stdio discovery of three tools, real protocol invocation of `find_customer`, rejection of an unknown tool, delivery of discovered tools to a scripted model boundary, HTTP endpoint behavior with a scripted runner, wrong-type rejection, and enforcement of a configured call limit.

They do not prove Azure OpenAI tool choice, live multi-step model behavior, production integrations, authentication, persistence, or restart recovery.

## Limitations

- Fixed in-memory enterprise data.
- One required string argument per model wrapper.
- Partial JSON-schema validation, not a complete validator.
- Arguments and results can expose sensitive enterprise data through logs and response telemetry.
- No authentication, authorization, retry, timeout policy, circuit breaker, persistent conversation, or distributed trace setup.
- Non-limit MCP failures normally propagate.

## Claims to avoid

Do not claim that there are four MCP servers or twelve tools; tools connect to production systems; argument/result values are excluded from logs; model routing quality is tested; schema enforcement is complete; MCP authorizes model requests; or results survive restart.

## Recommended sections

1. From bounded Agents to discoverable capabilities
2. MCP host, client, server, and tool roles
3. Running one assembly in two modes
4. Discovering the read-only catalog
5. Converting MCP tools into MAF functions
6. Model-selected and sequential tool calls
7. Deterministic catalog and schema checks
8. Bounding calls with `InvocationLedger`
9. Answers and invocation telemetry
10. Testing the real stdio boundary
11. Security and production limitations

## Reference instead of repeating

Reference Chapter 1 for basic `AIAgent` construction and deterministic authority boundaries. This chapter becomes the canonical explanation of MCP stdio discovery and invocation for Chapters 3 and 4.
