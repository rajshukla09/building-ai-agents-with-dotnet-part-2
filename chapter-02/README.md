# Chapter 2 — Enterprise Knowledge Assistant using MCP

This ASP.NET Core sample introduces MCP from the consumer side. It starts a separate MCP server process over stdio, discovers its read-only enterprise capabilities through the official MCP client, exposes those capabilities to a Microsoft Agent Framework Agent as model tools, lets the model select and sequence calls, and returns a synthesized answer with invocation telemetry.

The MCP protocol and stdio transport boundary are real. The GitHub, CRM, documentation, and SQL tool bodies return deterministic sample data; they are not connections to production enterprise systems. See [CODE-FLOW.md](CODE-FLOW.md) for the implementation walkthrough, policy details, and limitations.

## Configure Azure OpenAI

Use user secrets rather than committing credentials:

```bash
dotnet user-secrets --project src/EnterpriseKnowledgeAssistant.Api set AzureOpenAI:Endpoint https://YOUR-RESOURCE.openai.azure.com/
dotnet user-secrets --project src/EnterpriseKnowledgeAssistant.Api set AzureOpenAI:ApiKey YOUR-KEY
dotnet user-secrets --project src/EnterpriseKnowledgeAssistant.Api set AzureOpenAI:DeploymentName gpt-4o-mini
```

`src/EnterpriseKnowledgeAssistant.Api/appsettings.json` enables the demo MCP servers and sets `EnterpriseKnowledgeAssistant:MaxToolCalls` to five.

## Run

```bash
cd chapter-02
dotnet restore EnterpriseKnowledgeAssistant.sln
dotnet run --project src/EnterpriseKnowledgeAssistant.Api
```

Open Swagger at `http://localhost:5130/swagger` and call:

```text
POST /api/assistant/query
GET  /api/mcp/servers
GET  /api/mcp/tools
```

Example request body:

```json
{
  "query": "What open authentication problems are currently being tracked?"
}
```

For a multi-step example, try `What do we know about Contoso?`.

## Test

```bash
dotnet test EnterpriseKnowledgeAssistant.sln
```
