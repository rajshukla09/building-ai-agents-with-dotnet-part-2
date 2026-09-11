# Chapter 9 Part 3 Technical Writing Notes

These notes capture implemented concepts for the later manuscript pass. They are not polished chapter prose.

## Why Filtering Alone Is Not Enough

Metadata filtering protects the Incident boundary and removes clearly irrelevant categories, sources, services, and time ranges. As evidence grows, however, a valid filter can still yield dozens or hundreds of records. A Worker prompt must remain bounded even when every candidate is technically relevant.

## Ranking Evidence for the Next Agent

`IEvidenceRanker` assigns additive, explainable weights for exact Incident membership, component match, Agent-profile evidence priority, time-window overlap, source Agent, goal-term overlap, active-hypothesis terms, recency, and application-assigned importance. The score is a selection heuristic, not a calibrated probability. Stable tie breakers make identical inputs produce identical ordering.

## Enforcing a Context Budget

The target Agent profile and `RetrievalPipelineOptions` jointly cap evidence items, approximate characters, items per category, and evidence age. Budgeting walks the ranked list, retaining the best candidate that still fits. Evidence storage can grow independently of this fixed working-context budget.

## Deduplicating Repeated Findings

`IEvidenceDeduplicator` first rejects repeated Evidence IDs. It also builds a normalized key from source Agent, evidence type, component, and summary. The highest-ranked copy survives because deduplication runs after ranking. This handles repeated investigation findings without introducing clustering or another model.

## Deterministic vs Semantic Retrieval

The checkout scenario has strong structured keys: Incident, service, evidence type, source Agent, and time. Deterministic retrieval plus explainable term matching is sufficient for the current evidence. `ISemanticEvidenceSearch` is present behind `DisabledSemanticEvidenceSearch`, allowing a future implementation without changing storage, ranking, or assembly.

## Why Vector Search Is Only One Retrieval Strategy

Vector similarity cannot replace authorization and exact metadata constraints. Incident and service boundaries remain deterministic even if semantic search is later enabled. A vector store would add infrastructure without improving the current exact production-investigation queries enough to justify it.

## Building Explainable Context Bundles

Each selected item includes its Evidence ID, source, type, component, observed range, ranking score, named ranking factors, retrieval reason, deterministic-match flag, and semantic-contribution flag. These are safe application diagnostics, not hidden reasoning or chain-of-thought.

## Iterative Context Retrieval

A specialist can return `AdditionalContextRequirement` with a permitted category, component, time range, missing-information description, reason, and bounded keywords. Validation checks the Agent profile and structural limits. A successful retry builds a fresh focused bundle; it does not append the previous prompt.

## Preventing Retrieval Loops

Guardrails include maximum retrieval iterations per task, maximum investigation rounds, Agent invocation limits, context limits, timeout, cancellation propagation, and duplicate selected-ID detection. `CompactRetrievalOutcome` makes sufficient, invalid, timed-out, duplicate, and limit-reached outcomes observable.

## Keeping Stored Evidence Large and Working Context Small

Detailed findings remain in SQLite. Supervisor state stores evidence references, short hypotheses, task status, and compact retrieval outcomes. Retrieval diagnostics separately report total Incident evidence and selected working-context size, making the architectural boundary measurable.

## Testing Context Growth Under Load

The growth test stores 75 relevant Database records, requests only five, and verifies that the most valuable record survives ranking while the bundle respects both item and character limits. Other tests cover deduplication, Incident isolation, profile effects, adaptive retries, and the multi-Agent checkout investigation.

## Visualizing the Context Problem

The three colored size cards are a teaching visualization built by the application. They compare persisted evidence characters, serialized compact state characters, and the current selected bundle. MAF does not provide this dashboard automatically.

## Watching Evidence Grow Without Growing the Prompt

The Evidence Store table grows when each structured finding is persisted. The blue working-context card and every Agent budget remain bounded by the Part 3 pipeline. The UI reads both measurements from backend projections and does not truncate data to manufacture the comparison.

## Understanding the Retrieval Funnel

Each execution card renders actual `RetrievalDiagnostics`: total Incident evidence, deterministic candidates, ranked candidates, deduplicated candidates, selected records, and approximate selected characters. These stages are application retrieval behavior, not native MAF stages.

## Inspecting an Agent Context Bundle

The Context Bundle inspector shows the task, compact Incident summary, supplied hypotheses, selected evidence, provenance, ranking factors, and consumed budget. It intentionally omits system prompts, credentials, and hidden model reasoning.

## Explaining Why Evidence Was Selected

Ranking-factor chips expose safe application signals such as component, time-window, profile category, source Agent, recency, importance, goal terms, and hypothesis terms. The score is described as a ranking heuristic rather than a probability.

## Observing Adaptive Retrieval

Database retrieval round one displays its structured additional-context request. The timeline then shows validation and a second focused retrieval. Opening both execution cards demonstrates that the second bundle is rebuilt for `orders-database`; the first bundle is not appended.

## Comparing Stored Evidence with Working Context

The orange, green, and blue cards compare stored evidence, Supervisor state, and current working context. All sizes are approximate character counts. The UI does not label them as exact tokens.

## Tracing Context Through the Investigation

Polling refreshes authoritative backend projections while the deterministic demo runs. Timeline entries correspond to real observer callbacks at Agent selection, retrieval, bundle assembly, execution, evidence persistence, state update, adaptive request, and completion boundaries.

## Validating the Final Recommendation

The final panel separates the likely root cause, mitigation recommendation, status score, and supporting Evidence IDs. It displays structured synthesis output without exposing a reasoning transcript.

## Running the Complete Chapter 9 Demo

Run the API and Web projects in separate terminals. Open the Web URL, start the sample, inspect the funnel for each execution, compare the two Database retrieval rounds, filter the Evidence Store, and finish at the evidence-backed recommendation.

```powershell
dotnet run --project src/ContextEngineering.Api
dotnet run --project src/ContextEngineering.Web
```

## Shared Language Model Configuration

Chapter 9 follows the neighboring chapter convention of one server-side `AzureOpenAI` section containing endpoint, API key, and deployment name. `IInvestigationLanguageModel` is the single gateway used by all five specialists and final recommendation synthesis. Context selection, ranking, deduplication, and budgeting deliberately remain deterministic application services rather than model responsibilities. Tests switch the gateway to deterministic mode through configuration; production execution uses the configured Azure OpenAI deployment. Credentials belong in user secrets or environment configuration and never in the WebAssembly client.
