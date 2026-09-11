# Chapter 1 Plan: Human-in-the-Loop Claims Review

## 1. From Automated Workflows to Human Review

Chapter 1 extends Chapter 10's typed workflow with a claims process that can either complete automatically or wait for a reviewer. It introduces the end-to-end path from submission through Agents, deterministic rules, MAF orchestration, persistence, and later continuation.

**Main code/APIs:** `ClaimSubmissionRequest`, `IWorkflowRunMessage`, `WorkflowStartMessage`, `ClaimDraftMessage`, `ValidatedClaimMessage`, `RiskAssessmentMessage`, `ClaimDecisionResponse`

## 2. Configuring Real MAF AIAgents with Azure OpenAI

Configure Azure OpenAI through validated options and dependency injection, following the same client pattern as earlier chapters. Build an `AIAgent` from `AzureOpenAIClient`, `GetChatClient`, and `AsAIAgent`, while preserving replaceable application interfaces.

**Main code/APIs:** `AzureOpenAIOptions`, `AzureOpenAIClient`, `AzureKeyCredential`, `AIAgent`, `ChatClientAgentOptions`, `IClaimIntakeAgent`, `IRiskAssessmentAgent`, `Program.cs`

## 3. Normalizing Claims with the Intake AIAgent

The Intake Agent receives the original claim and returns a typed `ClaimDraft` using JSON-schema structured output. Its instructions limit it to normalization: it must preserve submitted facts and must not validate, assess risk, or make approval decisions.

**Main code/APIs:** `ClaimIntakeAgent`, `ClaimIntakeAgentRequest`, `ClaimDraft`, `RunAsync<ClaimDraft>`, `StructuredOutput.For<T>`, `ChatResponseFormat.ForJsonSchema<T>`

## 4. Enforcing Claim Rules Deterministically

After Agent normalization, C# validates required fields, supported claim types, amounts, dates, and evidence requirements. Successful validation produces `ValidatedClaim`; failures become `ClaimValidationException`, while nonfatal evidence concerns travel forward as warnings.

**Main code/APIs:** `ClaimValidator`, `IClaimValidator`, `ClaimValidationResult`, `ClaimValidationError`, `ClaimValidationExecutor`, `ValidatedClaim`

## 5. Assessing Risk with the Risk AIAgent

The Risk Agent receives only the validated claim and deterministic warnings, then returns `ClaimRiskAssessmentDraft` as structured output. C# validates and maps that draft to `ClaimRiskAssessment`, deliberately ignoring the model's `RequiresHumanReview` value so the LLM cannot control approval routing.

**Main code/APIs:** `RiskAssessmentAgent`, `RiskAssessmentAgentRequest`, `ClaimRiskAssessmentDraft`, `RunAsync<ClaimRiskAssessmentDraft>`, `RiskAssessmentAgent.ToAssessment`, `AgentResult<T>`

## 6. Applying the Deterministic Approval Policy

A separate C# policy decides whether human review is required using configured amount thresholds, risk level, risk score, and validation warnings. Low-risk claims proceed automatically; qualifying claims receive a typed `HumanApprovalRequirement` with explicit triggered rules.

**Main code/APIs:** `HumanApprovalPolicy`, `IHumanApprovalPolicy`, `HumanApprovalOptions`, `HumanApprovalRequirement`, `RiskAssessmentAgentExecutor`

## 7. Building the Conditional MAF Workflow

Typed Executors adapt each domain stage to MAF and record execution events and traces. Conditional workflow edges route the risk result either directly to `ClaimDecisionExecutor` or through `HumanApprovalRequestExecutor` and the human-approval port.

**Main code/APIs:** `ClaimExecutor<TIn, TOut>`, `ClaimIntakeAgentExecutor`, `ClaimValidationExecutor`, `RiskAssessmentAgentExecutor`, `WorkflowBuilder`, conditional `AddEdge<RiskAssessmentMessage>`, `ClaimReviewWorkflow.Create()`

## 8. Crossing the Human Boundary with RequestPort

For claims requiring review, `HumanApprovalRequestExecutor` creates a typed request containing claim and risk snapshots, reasons, rules, and expiry. `RequestPort<ClaimApprovalRequest, ClaimApprovalDecision>` emits a `RequestInfoEvent`, which the application captures and persists before disposing the streaming run.

**Main code/APIs:** `HumanApprovalRequestExecutor`, `ClaimApprovalRequest`, `RequestPort.Create`, `InProcessExecution.RunStreamingAsync`, `WatchStreamAsync`, `RequestInfoEvent`, `MafApprovalPayload`

## 9. Persisting and Hosting Workflow Execution

Starting a claim first stores a `Queued` run and then places a typed item on an in-memory channel. A single-reader `BackgroundService` creates a scoped workflow service for each start or resume item, while SQLite stores runs, approval snapshots, ordered events, final decisions, durations, errors, and Executor traces.

**Main code/APIs:** `ClaimWorkflowService.StartAsync`, `ClaimWorkflowQueue`, `ClaimWorkflowQueueItem`, `Channel<T>`, `ClaimWorkflowBackgroundService`, `ClaimsDbContext`, persistence record types

## 10. Approving, Rejecting, and Continuing from Persisted State

Reviewer endpoints persist exactly one effective decision and enqueue a typed resume command. The service validates identifiers and statuses, then starts a new decision-only MAF graph that reconstructs the final result from the persisted claim snapshot and reviewer decision.

**Main code/APIs:** `ClaimApprovalsController`, `EfClaimApprovalStore`, `ApprovalDecisionStatus`, `NativeApprovalResponseCommand`, `ClaimWorkflowService.ResumeAsync`, `CreateDecisionContinuation()`, `ClaimDecisionExecutor`

## 11. Recovery, Live UI, and Operational Boundaries

At startup, the worker requeues persisted Approved or Rejected decisions whose runs remain `WaitingForApproval`; it does not restore the original live MAF run. The Blazor UI reconstructs status and event history through HTTP polling; although a SignalR endpoint is mapped, the current code does not publish or subscribe to SignalR messages.

**Main code/APIs:** `RecoverDecidedApprovalsAsync`, `ClaimWorkflowsController`, `WorkflowEventSequenceLock`, `LiveWorkflow.razor`, `Approvals.razor`, `ApprovalDetails.razor`, `ApprovedClaims.razor`, `PeriodicTimer`, `MapHub`

## 12. Handling Failures and Proving the Design

Cover cancellation propagation, structured-output failures, Azure dependency and rate-limit failures, validation failures, rejection, expiry, duplicate decisions, conflicts, and invalid continuation commands. Tests substitute `IClaimIntakeAgent` and `IRiskAssessmentAgent`, proving deterministic behavior without Azure calls and confirming that persisted continuation is application-managed—not native durable workflow checkpoint recovery.

**Main code/APIs:** `AgentFailureFactory`, `AgentFailureKind`, `ApprovalExpiryService`, `CancelAsync`, `TestClaimIntakeAgent`, `TestRiskAssessmentAgent`, `Chapter02Tests`, `HumanApprovalFlowTests`
