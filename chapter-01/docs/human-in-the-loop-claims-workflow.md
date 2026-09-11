# Human-in-the-Loop Claims Workflow

Chapter 1 uses a fresh insurance claims scenario because approval is natural in claims processing: an agent can summarize and assess risk, while deterministic policy decides when a human must review.

```mermaid
flowchart TD
A[Claim Submission]-->B[Claim Intake Agent]-->C[Claim Validation]-->D[Risk Assessment Agent]-->E[Native MAF Approval RequestPort]
E-->F[Persist RequestInfoEvent and Wait]
F-->|Approved|G
F-->|Rejected|H[Rejected Business Outcome]
G-->I[Claim Decision Response]
```

```mermaid
sequenceDiagram
participant UI as Claims Review UI
participant API as Claims API
participant WF as MAF Workflow
participant Intake as Claim Intake Agent
participant Validation as Claim Validator
participant Risk as Risk Assessment Agent
participant Human as Human Reviewer
participant Decision as Claim Decision Executor
UI->>API: Submit claim
API->>WF: Start workflow and return 202
WF->>Intake: ClaimSubmissionRequest
Intake-->>WF: ClaimDraftMessage
WF->>Validation: deterministic checks
Validation-->>WF: ValidatedClaimMessage
WF->>Risk: assess risk
Risk-->>WF: RiskAssessmentMessage
WF->>WF: RequestPort emits RequestInfoEvent
WF-->>UI: Persist approval and publish ApprovalRequested
Human->>API: Approve or reject
API->>WF: Start persisted MAF decision continuation
alt Approved
WF->>Decision: ClaimApprovalDecision
Decision-->>WF: ClaimDecisionResponse
else Rejected
WF-->>UI: Rejected, not failed
end
```

The sample separates agent judgment from business authority. Intake and risk assessment are agents. Validation, approval policy, expiry, cancellation, rejection, and final claim decisions are deterministic application code.

The API does not keep the submit request open. Pending approvals are stored in SQLite and can be reloaded after refresh. Rejection is a valid business outcome rather than an infrastructure failure. The chapter is intentionally not Magentic orchestration: there is no supervisor, dynamic routing, fan-out/fan-in, MCP, or distributed workflow engine.
