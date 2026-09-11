# Chapter 5 — Multi-Agent Orchestration Patterns with MAF

This small .NET 9 sample compares native Microsoft Agent Framework 1.15.0 Sequential, Concurrent, Handoff, and Group Chat workflows against one software-release request. Its Blazor WebAssembly page uses the same input for all four patterns and visualizes each execution shape. See [CODE-FLOW.md](CODE-FLOW.md) for the decision guide and the Chapter 4 Magentic reference.

```bash
dotnet restore SoftwareReleaseReview.sln
dotnet test SoftwareReleaseReview.sln
dotnet run --project src/SoftwareReleaseReview.Api
dotnet run --project src/SoftwareReleaseReview.Web
```

Set `AzureOpenAI:Endpoint`, `AzureOpenAI:ApiKey`, and `AzureOpenAI:DeploymentName` with configuration or user secrets before invoking an endpoint. Swagger is available at the API's `/swagger`; the web project reads the API origin from `wwwroot/appsettings.json`. EF Core migrations create durable SQLite history at `App_Data/reviews.db`; the UI provides **Run Pattern**, **Run History**, and **Compare Runs** navigation.
