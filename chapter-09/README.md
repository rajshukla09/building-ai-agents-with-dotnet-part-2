# Chapter 9 — Production-Ready Context Engineering for Multi-Agent Systems

This chapter demonstrates production-oriented context engineering for a multi-agent incident investigation using Microsoft Agent Framework and .NET.

The scenario investigates the following production incident:

> Checkout latency increased significantly shortly after a production deployment.

The central design principle is simple:

> **The Evidence Store can grow. The Agent's working context should not.**

Instead of passing the complete investigation history to every Agent, the application stores detailed evidence durably and constructs a small, relevant, explainable `ContextBundle` for each specialist.

The implementation combines:

- durable evidence storage with SQLite and EF Core
- compact Supervisor execution state
- Agent-specific retrieval profiles
- typed deterministic retrieval specifications
- deterministic evidence filtering
- explainable evidence ranking
- deterministic deduplication
- bounded context budgets
- adaptive additional-context retrieval
- retrieval-loop and timeout controls
- retrieval diagnostics and a teaching UI

Semantic retrieval is represented by an abstraction but is intentionally disabled. The sample does not require embeddings or a vector database.

---

## Architecture

The investigation is coordinated by `InvestigationSupervisor`.

The Supervisor maintains a compact `SupervisorExecutionState`. Detailed findings are persisted separately as `EvidenceRecord` instances in SQLite.

For every specialist assignment, the Supervisor asks `IContextService` to construct an explicit, bounded `ContextBundle`.

The specialist receives the bundle rather than direct access to the Evidence Store.

```text
Incident / Agent Task
        |
        v
Agent Retrieval Profile
        |
        v
Retrieval Specification
        |
        v
Deterministic Filtering
        |
        v
Candidate Evidence
        |
        v
Explainable Ranking
        |
        v
Deduplication
        |
        v
Context Budget
        |
        v
ContextBundle
        |
        v
Specialist Agent
        |
        v
Structured Finding
        |
        +------> EvidenceRecord -> SQLite
        |
        +------> Compact Supervisor State
```

This separation allows the durable Evidence Store to accumulate detailed investigation history without continuously expanding the context supplied to every Agent.

---

## Specialist Agents

The investigation contains five specialists:

- `LogInvestigationAgent`
- `DeploymentAgent`
- `DatabaseAgent`
- `InfrastructureTelemetryAgent`
- `ApplicationAgent`

Each specialist is stateless with respect to the Evidence Store.

A specialist receives a `SpecialistRequest` containing its current task and a bounded `ContextBundle`. It does not query the Evidence Store directly.

The resulting `SpecialistFinding` is converted into a durable `EvidenceRecord`. The Supervisor retains only compact information required for continued coordination, including evidence references and active hypotheses.

---

## Evidence Store and Compact Supervisor State

SQLite and EF Core provide durable evidence storage.

Detailed evidence is represented by `EvidenceRecord` and includes information such as:

- incident ID
- source Agent
- evidence type
- component
- observed time range
- summary
- detailed content
- creation time

The Supervisor does not carry all of this content forward in its execution state.

`SupervisorExecutionState` instead maintains compact coordination information such as:

- current investigation goal
- incident summary
- pending investigations
- completed investigations
- affected components
- evidence references
- active hypotheses
- confidence
- retrieval outcomes
- investigation status

This creates an explicit boundary between **durable evidence** and **working orchestration state**.

---

## Agent-Specific Retrieval Profiles

Different specialists require different evidence.

Application-defined `AgentRetrievalProfile` instances describe the retrieval policy for each Agent.

A profile can constrain or prioritize characteristics such as:

- evidence types
- source Agents
- item limits
- context budget
- per-category limits
- evidence age

The model does not construct database queries.

`RetrievalSpecificationFactory` combines the current Agent, task, execution state, assignment constraints, and retrieval profile into a typed `RetrievalSpecification`.

This keeps retrieval policy under deterministic application control.

---

## Retrieval Pipeline

`EvidenceContextService` implements the context retrieval pipeline.

```text
Request
  -> Profile
  -> RetrievalSpecification
  -> Deterministic Filter
  -> Candidate Retrieval
  -> Rank
  -> Deduplicate
  -> Budget
  -> ContextBundle
```

The repository first filters evidence using explicit metadata boundaries such as:

- incident ID
- evidence type
- allowed source Agent
- component
- time range

The filtered candidate set is then ranked, deduplicated, and constrained by the configured context budget.

Only the selected evidence becomes part of the Agent's working context.

---

## Explainable Evidence Ranking

`ExplainableEvidenceRanker` ranks candidate evidence using deterministic, inspectable factors.

Current ranking factors include:

- exact incident boundary
- affected component match
- evidence-type priority from the Agent profile
- requested time-window overlap
- relevant source Agent
- current-goal term overlap
- active-hypothesis term overlap
- recency
- application-assigned importance

Each selected item carries its ranking score and the individual factors that contributed to that score.

The score is an application-defined heuristic used for ordering evidence. It is not a probability or model confidence score.

This makes evidence selection explainable without requiring another LLM call.

---

## Deterministic Deduplication

`DeterministicEvidenceDeduplicator` removes repeated evidence before the context budget is applied.

Duplicates are detected using:

- `EvidenceId`
- a normalized content key based on source Agent, evidence type, component, and summary

Because ranking occurs before deduplication, the higher-ranked representation is encountered first.

The number of removed duplicates is included in retrieval diagnostics.

---

## Context Budgets

The context service does not simply return every matching evidence item.

Selection is bounded using explicit limits including:

- maximum item count
- approximate character budget
- per-evidence-category limits

The service walks the ranked evidence in order and selects only items that fit within the configured limits.

The resulting `ContextBundle` includes `ContextBudgetUsage`, making configured and consumed context visible.

The character count is an application-level approximation used for deterministic budgeting. It is not an exact model-token count.

---

## ContextBundle

A specialist receives a `ContextBundle` rather than the Evidence Store.

The bundle contains the information required for the current investigation step, including:

- incident identity
- target Agent
- current task
- incident summary
- selected evidence
- evidence IDs
- summaries and detailed content
- provenance
- retrieval reason
- ranking scores and factors
- active hypotheses
- retrieval specification
- context-budget usage
- retrieval diagnostics

This makes the context supplied to each Agent explicit and inspectable.

---

## Adaptive Context Retrieval

A specialist can determine that its initial context is insufficient.

It can return an `AdditionalContextRequirement`.

The Supervisor validates this request before performing another retrieval.

The request remains constrained by application-defined retrieval policy. A specialist cannot provide arbitrary SQL or directly query the Evidence Store.

When the request is valid, the Supervisor performs another bounded retrieval and creates a **fresh `ContextBundle`** for the next specialist invocation.

```text
Initial ContextBundle
        |
        v
Specialist Agent
        |
        v
Enough context?
   /          \
 Yes          No
  |            |
  v            v
Finding     AdditionalContextRequirement
               |
               v
         Validate Request
               |
               v
        Bounded Retrieval
               |
               v
        Fresh ContextBundle
               |
               v
          Specialist Again
```

The implementation therefore supports adaptive retrieval without allowing Agent context to grow indefinitely through repeated prompt accumulation.

---

## Retrieval Safety Controls

Adaptive retrieval is bounded by deterministic controls.

`InvestigationOptions` includes limits for:

- `MaximumSpecialistInvocations`
- `MaximumRetrievalIterationsPerTask`
- `MaximumInvestigationRounds`
- `RetrievalTimeoutSeconds`

The Supervisor also detects repeated evidence selections so that an Agent cannot continuously request effectively identical context.

Retrieval outcomes record conditions including:

- sufficient initial context
- additional context provided
- invalid additional-context request
- duplicate retrieval stopped
- retrieval timeout
- iteration limit reached

Cancellation is propagated through the investigation and retrieval operations.

These controls keep adaptive context retrieval bounded even when Agent output requests additional information.

---

## Retrieval Diagnostics and Observability

Context selection is observable at the application level.

`RetrievalDiagnostics` records information such as:

- total evidence available for the incident
- deterministic candidate count
- semantic candidate count
- duplicates removed
- ranked candidate count
- selected item count
- approximate selected characters
- configured item limit
- configured context budget
- retrieval duration
- retrieval iteration
- selected evidence IDs

This makes it possible to inspect not only what an Agent received, but how the application arrived at that context.

---

## Teaching UI

The chapter includes a Blazor WebAssembly teaching UI.

The UI is designed to make context engineering visible rather than hiding retrieval behind an Agent call.

It includes views for investigating concepts such as:

- Evidence Store growth
- compact Supervisor state
- Agent working-context size
- retrieval funnel counts
- context-budget consumption
- selected evidence
- ranking factors
- context bundle inspection
- adaptive retrieval iterations
- investigation activity
- final investigation results

`ContextBundleInspector` provides a dedicated view into the bounded context assembled for an Agent.

The UI is an application-level teaching and observability surface. It is not a distributed tracing system.

---

## Semantic Retrieval Boundary

The architecture includes `ISemanticEvidenceSearch`.

The current implementation uses `DisabledSemanticEvidenceSearch`, which returns no semantic candidates.

Therefore, the chapter does **not** currently perform:

- embedding generation
- vector indexing
- vector similarity search
- hybrid vector retrieval
- reranking with an embedding or language model

For the focused incident scenario, deterministic metadata filtering plus explainable application-level ranking is sufficient to demonstrate the context-engineering architecture.

The abstraction provides an extension point where semantic retrieval could be introduced later without changing the fundamental context-bounding design.

---

## Model and Deterministic Responsibilities

The architecture deliberately separates model-driven reasoning from deterministic application control.

The specialist Agents can use model-driven reasoning to analyze the bounded context and produce structured findings.

The application remains responsible for:

- selecting the configured specialist from the assignment
- defining retrieval profiles
- creating retrieval specifications
- querying evidence
- ranking evidence
- deduplicating evidence
- enforcing context budgets
- validating additional-context requests
- limiting retrieval iterations
- detecting duplicate retrieval
- enforcing invocation limits
- enforcing retrieval timeouts
- persisting evidence
- maintaining compact execution state

This keeps important retrieval and safety boundaries outside the language model.

---

## Project Structure

The chapter contains two applications:

```text
src/
  ContextEngineering.Api/
  ContextEngineering.Web/

tests/
  ContextEngineering.Tests/
```

Important API implementation areas include:

```text
Agents/
Context/
Models/
Observability/
Orchestration/
Persistence/
```

Key implementation files include:

- `InvestigationSupervisor.cs`
- `EvidenceContextService.cs`
- `EvidenceRetrievalPipeline.cs`
- `RetrievalProfiles.cs`
- `RetrievalSpecificationFactory.cs`
- `AdditionalContextValidator.cs`
- `EvidenceRepository.cs`
- `EvidenceRecord.cs`
- `InvestigationViews.cs`

The Web project includes the teaching UI and `ContextBundleInspector`.

---

## Tests

The test project includes coverage organized around:

- context architecture
- retrieval and context behavior
- failure handling
- language-model configuration
- UI observability

Important test files include:

- `ContextArchitectureTests.cs`
- `DemoFailureHandlingTests.cs`
- `LanguageModelConfigurationTests.cs`
- `UiObservabilityTests.cs`

The tests should be interpreted as verification of the behavior explicitly exercised by those test cases. They do not establish production-scale retrieval quality, model quality, semantic-search behavior, or distributed-system guarantees that are not implemented by the sample.

---

## Key Design Principle

The chapter's core architectural lesson is:

> **Persist detailed evidence, maintain compact coordination state, and construct a fresh bounded working context for each Agent task.**

Context engineering is therefore not simply prompt construction.

It is an application architecture for deciding:

- what information is available
- what information is relevant to a particular Agent
- why that information was selected
- how much information the Agent is allowed to receive
- when additional context may be retrieved
- when retrieval must stop

That separation allows a multi-agent system's durable knowledge to grow without forcing every Agent's working context to grow with it.