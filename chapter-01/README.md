# Chapter 1 — Human-in-the-Loop Workflows with Microsoft Agent Framework

This chapter adds a focused claims review application under `chapter-01/` with an ASP.NET Core API, Blazor WebAssembly client, shared contracts, EF Core SQLite persistence, SignalR endpoint, and tests.

See `docs/human-in-the-loop-claims-workflow.md` for diagrams and walkthrough notes.
# Human approval resumption

The workflow uses the Microsoft Agent Framework native
`RequestPort.Create<ClaimApprovalRequest, ClaimApprovalDecision>()` boundary. The API consumes the
native `RequestInfoEvent`, persists its MAF request ID and typed request snapshot, and records the
approval boundary as `WaitingForApproval`. The streaming execution is then disposed immediately.
Approval and rejection endpoints persist a typed decision and enqueue a continuation that starts
at the remaining MAF `ClaimDecisionExecutor`; intake, validation, and risk executors are not rerun.

The installed in-process workflow package does not expose a serializable `StreamingRun` checkpoint
through the API used by this sample. The durable continuation therefore uses the persisted typed
approval, claim snapshot, and risk snapshot as the input to a small MAF continuation workflow.
This survives application restarts and retains no streaming run, callback, delegate, or DI scope
while a reviewer decides. It is an application-level MAF continuation rather than a native MAF
checkpoint restoration; the distinction is explicit because this package version cannot provide
the latter.
