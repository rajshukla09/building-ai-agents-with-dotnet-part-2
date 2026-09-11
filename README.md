# Building AI Agents with .NET

## Part 2: Advanced Agentic Architecture and Production Workflows

This repository contains the companion source code for **Building AI Agents with .NET — Part 2: Advanced Agentic Architecture and Production Workflows** by **Raj Shukla**.

**Part 2 continues the journey from Part 1**, moving beyond foundational AI agent development into advanced agentic architecture and production-oriented workflows using C#, .NET, and Microsoft Agent Framework.

The book explores human-in-the-loop workflows, Model Context Protocol (MCP), multi-agent orchestration, durable workflows, observability with OpenTelemetry, Agent-to-Agent (A2A) communication, and production-ready context engineering.

Each `chapter-XX` directory is an independent snapshot containing the implementation, tests, and chapter-specific documentation.

---

## Chapters

| Chapter | Topic |
| --- | --- |
| 01 | Human-in-the-Loop Workflows with Microsoft Agent Framework |
| 02 | Enterprise Knowledge Assistant using MCP |
| 03 | Dynamic Multi-Agent Orchestration |
| 04 | Magentic Orchestration |
| 05 | Multi-Agent Orchestration Patterns with MAF |
| 06 | Durable Purchase Approval |
| 07 | OpenTelemetry for Workflows |
| 08 | Agent-to-Agent (A2A) |
| 09 | Production-Ready Context Engineering for Multi-Agent Systems |

---

## Prerequisites

Depending on the chapter, you may need:

- The .NET SDK version required by the selected chapter
- An Azure OpenAI resource and model deployment for chapters that use a live language model
- Git
- Chapter-specific infrastructure described in the corresponding chapter README

---

## Configuration

Tracked configuration contains safe placeholders only.

Configure credentials using .NET User Secrets or environment variables rather than storing credentials in source control.

For example:

```powershell
cd chapter-01

dotnet user-secrets --project src/ClaimsReview.Api set "AzureOpenAI:Endpoint" "https://YOUR-RESOURCE.openai.azure.com/"
dotnet user-secrets --project src/ClaimsReview.Api set "AzureOpenAI:ApiKey" "YOUR-API-KEY"
dotnet user-secrets --project src/ClaimsReview.Api set "AzureOpenAI:DeploymentName" "YOUR-DEPLOYMENT-NAME"
```

Never commit API keys, tokens, passwords, private endpoints, local databases, exported user secrets, or `.env` files.

---

## Build and Test

Each chapter is independent. Start with the chapter's README, then restore, build, and test its solution or projects.

For example:

```powershell
cd chapter-01

dotnet restore ClaimsReview.sln
dotnet build ClaimsReview.sln --no-restore
dotnet test ClaimsReview.sln --no-build
```

Some chapters require additional infrastructure:

- Chapter 06 requires a Durable Task Scheduler.
- Chapter 07 expects an OTLP-compatible collector for live telemetry.

Refer to the individual chapter documentation for the exact setup.

---

## Part 1 — Start Here

If you are new to Microsoft Agent Framework or want to follow the series from the beginning, start with:

### Building AI Agents with .NET — Part 1

**Microsoft Agent Framework, C#, and Production-Ready Agentic Workflows**

Part 1 introduces the foundations of building AI agents with C# and .NET and progresses toward production-oriented agentic workflows.

Part 2 builds on those foundations and moves into advanced orchestration, human-in-the-loop systems, durability, observability, distributed agents, and production context engineering.

**Amazon:**  
https://www.amazon.com/dp/B0HFMX5V9P

**GitHub Companion Repository:**  
https://github.com/rajshukla09/building-ai-agents-with-dotnet-part-1

---

## Part 2 Book

This repository accompanies:

**Building AI Agents with .NET — Part 2: Advanced Agentic Architecture and Production Workflows**

The Amazon link for Part 2 will be added here after publication.

---

## Repository Structure

```text
building-ai-agents-with-dotnet-part-2/
├── chapter-01/
├── chapter-02/
├── chapter-03/
├── chapter-04/
├── chapter-05/
├── chapter-06/
├── chapter-07/
├── chapter-08/
├── chapter-09/
├── .gitignore
├── LICENSE
└── README.md
```

Each chapter is intentionally self-contained so readers can explore, build, test, and modify the examples independently.

---

## About the Series

**Building AI Agents with .NET** is a practical series focused on designing and implementing AI agents and agentic systems using C#, .NET, Microsoft Agent Framework, and the Microsoft AI ecosystem.

Across the series, the books cover topics including:

- AI agent fundamentals
- Tool and function calling
- Structured outputs
- Conversation and state management
- Agent workflows
- Human-in-the-loop systems
- Model Context Protocol (MCP)
- Multi-agent orchestration
- Durable execution
- Observability
- Agent-to-Agent (A2A) communication
- Context engineering
- Testing and production readiness

The examples emphasize working implementations and explicit architectural boundaries rather than isolated code snippets.

---

## Author

**Raj Shukla**

Software Architect and Generative AI Engineer focused on .NET, Azure, AI agents, agentic workflows, and production AI systems.

**GitHub:**  
https://github.com/rajshukla09

---

## License

The source code in this repository is licensed under the **MIT License**.

See [LICENSE](LICENSE) for details.

The book content, text, diagrams, and other published material are separately copyrighted and are not covered by the MIT License unless explicitly stated otherwise.

---

## Feedback and Issues

If you find an issue in an example, documentation, or chapter implementation, please open an issue in this repository.

When reporting an issue, include the chapter number and enough information to reproduce the problem.

---

**Building AI Agents with .NET — Part 2**  
*Advanced Agentic Architecture and Production Workflows*

© 2026 Raj Shukla
