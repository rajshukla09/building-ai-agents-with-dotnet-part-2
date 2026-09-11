namespace PurchaseApproval.Api;

public enum PurchaseStatus
{
    Running,
    WaitingForApproval,
    Completed,
    Rejected,
    Failed
}

public sealed record SubmitPurchase(string Description, decimal Amount);

public sealed record PurchaseStarted(Guid RunId, PurchaseStatus Status);

public sealed record DecidePurchase(string? Manager = null);

public sealed record PurchaseView(
    Guid RunId,
    string Description,
    decimal Amount,
    PurchaseStatus Status,
    string? PendingStep,
    string[] CompletedSteps,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? OrderReference,
    int ValidateExecutions,
    int BudgetExecutions,
    int ProcurementExecutions);

public sealed record StartMessage(Guid RunId, string Description, decimal Amount);

public sealed record ValidatedMessage(Guid RunId, string Description, decimal Amount);

public sealed record BudgetMessage(Guid RunId, string Description, decimal Amount);

public sealed record ApprovalRequest(Guid RunId, string Description, decimal Amount);

public sealed record ApprovalDecision(Guid RunId, bool Approved, string Manager);

public sealed record ProcurementMessage(Guid RunId, string Description, decimal Amount);

public sealed record CompletedMessage(Guid RunId, string OrderReference);
