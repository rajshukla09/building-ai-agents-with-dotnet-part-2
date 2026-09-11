using Microsoft.Agents.AI.Workflows;
using Microsoft.EntityFrameworkCore;

namespace PurchaseApproval.Api;

public abstract class PurchaseExecutor<TIn, TOut>(
    string id,
    string connectionString,
    TimeProvider clock) : Executor<TIn, TOut>(id)
{
    private readonly DbContextOptions<PurchasesDb> _options =
        new DbContextOptionsBuilder<PurchasesDb>().UseSqlite(connectionString).Options;

    protected PurchasesDb CreateDb() => new(_options);

    protected async Task<PurchaseRun> Run(Guid id, CancellationToken ct)
    {
        await using var db = CreateDb();
        return await db.Runs.SingleAsync(run => run.Id == id, ct);
    }

    protected async Task Save(
        PurchaseRun run,
        Action<PurchaseRun>? change,
        CancellationToken ct)
    {
        await using var db = CreateDb();
        var current = await db.Runs.SingleAsync(item => item.Id == run.Id, ct);
        change?.Invoke(current);

        current.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }
}

public sealed class ValidatePurchase(
    string connectionString,
    TimeProvider time) : PurchaseExecutor<StartMessage, ValidatedMessage>(nameof(ValidatePurchase), connectionString, time)
{
    public override async ValueTask<ValidatedMessage> HandleAsync(
        StartMessage message,
        IWorkflowContext context,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(message.Description) || message.Amount <= 0)
        {
            throw new ArgumentException("Description and a positive amount are required.");
        }

        var run = await Run(message.RunId, ct);
        await Save(run, current => current.ValidateExecutions++, ct);
        return new(message.RunId, message.Description, message.Amount);
    }
}

public sealed class BudgetCheck(
    string connectionString,
    TimeProvider time) : PurchaseExecutor<ValidatedMessage, BudgetMessage>(nameof(BudgetCheck), connectionString, time)
{
    public override async ValueTask<BudgetMessage> HandleAsync(
        ValidatedMessage message,
        IWorkflowContext context,
        CancellationToken ct = default)
    {
        if (message.Amount > 100000)
        {
            throw new InvalidOperationException("Purchase exceeds the demo budget.");
        }

        var run = await Run(message.RunId, ct);
        await Save(run, current => current.BudgetExecutions++, ct);
        return new(message.RunId, message.Description, message.Amount);
    }
}

public sealed class AwaitManagerApproval() : Executor<BudgetMessage, ApprovalRequest>(nameof(AwaitManagerApproval))
{
    public override async ValueTask<ApprovalRequest> HandleAsync(
        BudgetMessage message,
        IWorkflowContext context,
        CancellationToken ct = default)
    {
        return new(message.RunId, message.Description, message.Amount);
    }
}

public sealed class Procurement(
    string connectionString,
    TimeProvider time) : PurchaseExecutor<ApprovalDecision, CompletedMessage>(nameof(Procurement), connectionString, time)
{
    private readonly TimeProvider _time = time;

    public override async ValueTask<CompletedMessage> HandleAsync(
        ApprovalDecision message,
        IWorkflowContext context,
        CancellationToken ct = default)
    {
        await using var db = CreateDb();
        if (!message.Approved)
        {
            var rejected = await db.Runs.SingleAsync(item => item.Id == message.RunId, ct);
            rejected.Status = PurchaseStatus.Rejected;
            rejected.UpdatedAt = _time.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return new(message.RunId, "Rejected");
        }
        var key = $"{message.RunId}:Procurement";
        var record = await db.Procurements.SingleOrDefaultAsync(
            item => item.IdempotencyKey == key,
            ct);
        if (record is null)
        {
            record = new()
            {
                RunId = message.RunId,
                IdempotencyKey = key,
                OrderReference = $"PO-{message.RunId:N}"
            };
            db.Procurements.Add(record);

            var run = await db.Runs.SingleAsync(item => item.Id == message.RunId, ct);
            run.ProcurementExecutions++;
            run.OrderReference = record.OrderReference;
            run.Status = PurchaseStatus.Completed;
            run.UpdatedAt = _time.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }

        return new(message.RunId, record.OrderReference);
    }
}

public sealed class PurchaseWorkflow(
    Workflow workflow)
{
    public Workflow Definition => workflow;

    public static PurchaseWorkflow Create(string connectionString, TimeProvider time)
    {
        var validate = new ValidatePurchase(connectionString, time);
        var budget = new BudgetCheck(connectionString, time);
        var approval = new AwaitManagerApproval();
        var procurement = new Procurement(connectionString, time);
        var port = RequestPort.Create<ApprovalRequest, ApprovalDecision>("manager-approval");
        var workflow = new WorkflowBuilder(validate)
            .AddEdge(validate, budget)
            .AddEdge(budget, approval)
            .AddEdge(approval, port)
            .AddEdge(port, procurement)
            .WithOutputFrom(procurement)
            .WithName("PurchaseApproval")
            .Build();
        return new(workflow);
    }
}
