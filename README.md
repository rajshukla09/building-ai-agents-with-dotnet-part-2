# Building AI Agents with .NET

## Part 2: Advanced Agentic Architecture and Production Workflows

This repository contains the companion source code for Part 2 of *Building AI Agents with .NET*. The nine `chapter-XX` directories are independent snapshots containing the implementation, tests, and chapter-specific documentation.

The examples were extracted from Chapters 11–19 of the original working repository and renumbered as Chapters 01–09 for this standalone Part 2 repository.

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

## Prerequisites

- The .NET SDK version required by the selected chapter
- An Azure OpenAI resource and model deployment for chapters that use a live language model
- Git
- Chapter-specific infrastructure described in that chapter's README

## Configuration

Tracked configuration contains safe placeholders only. Configure credentials with .NET user secrets or environment variables. For example:

```powershell
cd chapter-01
dotnet user-secrets --project src/ClaimsReview.Api set "AzureOpenAI:Endpoint" "https://YOUR-RESOURCE.openai.azure.com/"
dotnet user-secrets --project src/ClaimsReview.Api set "AzureOpenAI:ApiKey" "YOUR-API-KEY"
dotnet user-secrets --project src/ClaimsReview.Api set "AzureOpenAI:DeploymentName" "YOUR-DEPLOYMENT-NAME"
```

Never commit API keys, tokens, passwords, private endpoints, local databases, exported user secrets, or `.env` files.

## Build and test

Each chapter is independent. Start with its README, then restore, build, and test its solution or projects. For example:

```powershell
cd chapter-01
dotnet restore ClaimsReview.sln
dotnet build ClaimsReview.sln --no-restore
dotnet test ClaimsReview.sln --no-build
```

Some chapters require external infrastructure. Chapter 6 requires a Durable Task Scheduler, and Chapter 7 expects an OTLP-compatible collector for live telemetry.

## Part 1

Part 1 covers Chapters 1–10 of the original book sequence and is published separately in the `building-ai-agents-with-dotnet-part-1` repository.

## Author

Raj Shukla
