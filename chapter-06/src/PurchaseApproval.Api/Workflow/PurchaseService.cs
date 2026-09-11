using Microsoft.Agents.AI.DurableTask.Workflows;
using Microsoft.DurableTask.Client;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace PurchaseApproval.Api;

public sealed class PurchaseService(
    IDbContextFactory<PurchasesDb> factory,
    PurchaseWorkflow workflow,
    IWorkflowClient workflows,
    DurableTaskClient durableTasks,
    TimeProvider clock,
    ILogger<PurchaseService> logger)
{
    public async Task<PurchaseStarted> Start(SubmitPurchase request, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            db.Runs.Add(new()
            {
                Id = id,
                Description = request.Description,
                Amount = request.Amount,
                Status = PurchaseStatus.Running,
                CreatedAt = clock.GetUtcNow(),
                UpdatedAt = clock.GetUtcNow()
            });
            await db.SaveChangesAsync(ct);
        }

        await workflows.RunAsync(workflow.Definition, new StartMessage(id, request.Description, request.Amount), id.ToString(), ct);
        await WaitUntilApprovalOrCompletion(id.ToString(), ct);
        logger.LogInformation("Durable workflow {InstanceId} started", id);
        return new(id, PurchaseStatus.WaitingForApproval);
    }

    public async Task<PurchaseView?> Get(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var run = await db.Runs.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, ct);
        if (run is null)
        {
            return null;
        }

        var metadata = await durableTasks.GetInstanceAsync(id.ToString(), true, ct);
        return metadata is null ? null : View(run, metadata);
    }

    public async Task<PurchaseView?> Decide(Guid id, bool approved, string manager, CancellationToken ct)
    {
        var metadata = await durableTasks.GetInstanceAsync(id.ToString(), true, ct);
        if (metadata is null)
        {
            return null;
        }

        if (metadata.IsCompleted)
        {
            return await Get(id, ct);
        }

        var eventName = PendingApprovalEvent(metadata);
        if (eventName is null)
        {
            return null;
        }

        await durableTasks.RaiseEventAsync(
            id.ToString(),
            eventName,
            JsonSerializer.Serialize(new ApprovalDecision(id, approved, manager)),
            ct);
        await durableTasks.WaitForInstanceCompletionAsync(id.ToString(), true, ct);
        logger.LogInformation("Durable workflow {InstanceId} received manager decision", id);
        return await Get(id, ct);
    }

    private async Task WaitUntilApprovalOrCompletion(string instanceId, CancellationToken ct)
    {
        while (true)
        {
            var metadata = await durableTasks.GetInstanceAsync(instanceId, true, ct);
            if (metadata?.IsCompleted == true || (metadata is not null && PendingApprovalEvent(metadata) is not null))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
        }
    }

    private static string? PendingApprovalEvent(OrchestrationMetadata metadata)
    {
        if (metadata.SerializedCustomStatus is not { Length: > 0 } status)
        {
            return null;
        }

        using var document = JsonDocument.Parse(status);
        if (!TryGetProperty(document.RootElement, "pendingEvents", out var pendingEvents) ||
            pendingEvents.GetArrayLength() != 1 ||
            !TryGetProperty(pendingEvents[0], "eventName", out var eventName))
        {
            return null;
        }

        return eventName.GetString();
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static PurchaseView View(PurchaseRun run, OrchestrationMetadata metadata)
    {
        var pending = PendingApprovalEvent(metadata);
        var status = metadata.RuntimeStatus switch
        {
            OrchestrationRuntimeStatus.Completed => run.Status,
            OrchestrationRuntimeStatus.Failed or OrchestrationRuntimeStatus.Terminated => PurchaseStatus.Failed,
            _ when pending is not null => PurchaseStatus.WaitingForApproval,
            _ => PurchaseStatus.Running
        };
        var steps = new List<string>();
        if (run.ValidateExecutions > 0) steps.Add("ValidatePurchase");
        if (run.BudgetExecutions > 0) steps.Add("BudgetCheck");
        if (pending is not null || metadata.IsCompleted) steps.Add("ManagerApproval");
        if (run.ProcurementExecutions > 0) steps.Add("Procurement");
        if (metadata.IsCompleted) steps.Add("Completed");

        return new(
            run.Id,
            run.Description,
            run.Amount,
            status,
            pending is null ? null : "ManagerApproval",
            [.. steps],
            run.CreatedAt,
            run.UpdatedAt,
            run.OrderReference,
            run.ValidateExecutions,
            run.BudgetExecutions,
            run.ProcurementExecutions);
    }
}
