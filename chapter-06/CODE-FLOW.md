# Chapter 6 code flow

```text
POST purchase
  -> IWorkflowClient.RunAsync(workflow, input, instanceId, cancellationToken)
  -> ValidatePurchase durable activity
  -> BudgetCheck durable activity
  -> RequestPort<ApprovalRequest, ApprovalDecision>
  -> Durable Task WaitForExternalEvent
  -> process may stop and restart
  -> DurableTaskClient.RaiseEventAsync(instanceId, eventName, decision, cancellationToken)
  -> Procurement durable activity
  -> durable orchestration completes
```

## Native APIs used

The application calls the APIs exposed by the locally restored packages:

- `IServiceCollection.ConfigureDurableWorkflows(Action<DurableWorkflowOptions>, Action<IDurableTaskWorkerBuilder>, Action<IDurableTaskClientBuilder>)`
- `DurableWorkflowOptions.AddWorkflow(Workflow)`
- `IDurableTaskWorkerBuilder.UseDurableTaskScheduler(string connectionString, Action<DurableTaskSchedulerWorkerOptions>)`
- `IDurableTaskClientBuilder.UseDurableTaskScheduler(string connectionString, Action<DurableTaskSchedulerClientOptions>)`
- `IWorkflowClient.RunAsync<T>(Workflow, T, string instanceId, CancellationToken)`
- `DurableTaskClient.GetInstanceAsync(string instanceId, bool getInputsAndOutputs, CancellationToken)`
- `DurableTaskClient.RaiseEventAsync(string instanceId, string eventName, object eventPayload, CancellationToken)`
- `DurableTaskClient.WaitForInstanceCompletionAsync(string instanceId, bool getInputsAndOutputs, CancellationToken)`

The MAF durable dispatcher converts the workflow's native `RequestPort.Create<ApprovalRequest, ApprovalDecision>("manager-approval")` into `TaskOrchestrationContext.WaitForExternalEvent<T>(string, CancellationToken)`. The exact generated event name is published in the orchestration custom status and is used when raising the decision after restart.

## Persistence boundaries

Durable Task Scheduler owns orchestration history, execution position, pending external events, status, and result. SQLite contains only business/audit data: purchase details, execution counters, final business outcome, and the procurement record. It is not used to reconstruct or select a continuation.

Procurement retains the unique idempotency key `{instanceId}:Procurement`; durable replay does not make an external business write atomic with orchestration checkpointing.

## Local validation

Run a Durable Task Scheduler on `localhost:8080` with task hub `default`, or override `ConnectionStrings:DurableTaskScheduler`. Then run `dotnet test`. The integration test disposes the first API/worker host while the durable instance is waiting, creates a new host, raises approval, and asserts validation, budget check, and procurement each executed exactly once. If no scheduler is listening, that external integration test is reported as skipped rather than replaced by an in-memory workflow.
