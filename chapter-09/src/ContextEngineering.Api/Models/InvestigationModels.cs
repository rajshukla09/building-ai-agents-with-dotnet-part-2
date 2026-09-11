namespace ContextEngineering.Api.Models;

public enum InvestigationStatus
{
    Pending,
    Running,
    Completed,
    LimitReached,
    Failed
}

public enum RetrievalAttemptStatus
{
    Sufficient,
    AdditionalContextProvided,
    InvalidRequest,
    IterationLimitReached,
    DuplicateRetrievalStopped,
    TimedOut
}

public enum EvidenceType
{
    Log,
    Deployment,
    Database,
    Telemetry,
    Application
}

public sealed record EvidenceReference(
    Guid EvidenceId,
    string SourceAgent,
    EvidenceType EvidenceType,
    string Summary);

public sealed record ActiveHypothesis(
    string Description,
    double Confidence,
    IReadOnlyList<Guid> SupportingEvidenceIds);

public sealed class SupervisorExecutionState
{
    public required Guid IncidentId { get; init; }

    public required string CurrentGoal { get; set; }

    public string IncidentSummary { get; set; } = string.Empty;

    public List<string> AffectedComponents { get; } = [];

    public DateTimeOffset? IncidentFrom { get; set; }

    public DateTimeOffset? IncidentTo { get; set; }

    public List<string> CompletedInvestigations { get; } = [];

    public List<string> PendingInvestigations { get; } = [];

    public List<ActiveHypothesis> ActiveHypotheses { get; } = [];

    public List<EvidenceReference> EvidenceReferences { get; } = [];

    public InvestigationStatus Status { get; set; } = InvestigationStatus.Pending;

    public double Confidence { get; set; }

    public List<CompactRetrievalOutcome> RetrievalOutcomes { get; } = [];
}

public sealed record CompactRetrievalOutcome(
    string Agent,
    int Iterations,
    RetrievalAttemptStatus Status,
    IReadOnlyList<Guid> SelectedEvidenceIds);

public sealed record InvestigationAssignment(
    string TargetAgent,
    string Task,
    IReadOnlyCollection<EvidenceType>? AllowedEvidenceTypes = null,
    string? Component = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    IReadOnlyCollection<string>? SourceAgents = null);

public sealed record SpecialistRequest(
    Guid IncidentId,
    string CurrentTask,
    ContextBundle Context,
    IReadOnlyDictionary<string, string> Constraints);

public sealed record SpecialistFinding(
    string SourceAgent,
    EvidenceType EvidenceType,
    string Component,
    DateTimeOffset? ObservedFrom,
    DateTimeOffset? ObservedTo,
    string Summary,
    string DetailedContent,
    string? Hypothesis,
    double Confidence,
    AdditionalContextRequirement? AdditionalContextRequired = null);

public sealed record AdditionalContextRequirement(
    EvidenceType RequiredEvidenceType,
    string Component,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string MissingInformation,
    string Reason,
    IReadOnlyCollection<string>? Keywords = null);
