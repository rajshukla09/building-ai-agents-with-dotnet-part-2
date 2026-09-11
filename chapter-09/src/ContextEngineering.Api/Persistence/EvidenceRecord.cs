using ContextEngineering.Api.Models;

namespace ContextEngineering.Api.Persistence;

public sealed class EvidenceRecord
{
    public Guid EvidenceId { get; set; }

    public Guid IncidentId { get; set; }

    public required string SourceAgent { get; set; }

    public EvidenceType EvidenceType { get; set; }

    public required string Component { get; set; }

    public DateTimeOffset? ObservedFrom { get; set; }

    public DateTimeOffset? ObservedTo { get; set; }

    public required string Summary { get; set; }

    public required string DetailedContent { get; set; }

    public int Importance { get; set; } = 3;

    public DateTimeOffset CreatedAt { get; set; }
}
