using ContextEngineering.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ContextEngineering.Api.Persistence;

public interface IEvidenceRepository
{
    Task AddAsync(EvidenceRecord evidence, CancellationToken cancellationToken);

    Task<int> CountForIncidentAsync(Guid incidentId, CancellationToken cancellationToken);

    Task<IReadOnlyList<EvidenceRecord>> QueryAsync(
        Guid incidentId,
        IReadOnlyCollection<EvidenceType>? evidenceTypes,
        string? component,
        int maximumItems,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EvidenceRecord>> QueryAsync(
        RetrievalSpecification specification,
        CancellationToken cancellationToken);
}

public sealed class SqliteEvidenceRepository(IDbContextFactory<EvidenceDbContext> contextFactory)
    : IEvidenceRepository
{
    public async Task AddAsync(
        EvidenceRecord evidence,
        CancellationToken cancellationToken)
    {
        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        database.Evidence.Add(evidence);
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> CountForIncidentAsync(
        Guid incidentId,
        CancellationToken cancellationToken)
    {
        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await database.Evidence.CountAsync(
            item => item.IncidentId == incidentId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<EvidenceRecord>> QueryAsync(
        Guid incidentId,
        IReadOnlyCollection<EvidenceType>? evidenceTypes,
        string? component,
        int maximumItems,
        CancellationToken cancellationToken)
    {
        var specification = new RetrievalSpecification(
            incidentId,
            "LegacyCaller",
            "Direct evidence query",
            evidenceTypes ?? Enum.GetValues<EvidenceType>(),
            [],
            string.IsNullOrWhiteSpace(component) ? [] : [component],
            null,
            null,
            maximumItems,
            int.MaxValue,
            "Direct repository query");

        return await QueryAsync(specification, cancellationToken);
    }

    public async Task<IReadOnlyList<EvidenceRecord>> QueryAsync(
        RetrievalSpecification specification,
        CancellationToken cancellationToken)
    {
        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = database.Evidence
            .AsNoTracking()
            .Where(item => item.IncidentId == specification.IncidentId);

        if (specification.EvidenceTypes.Count > 0)
        {
            query = query.Where(item => specification.EvidenceTypes.Contains(item.EvidenceType));
        }

        if (specification.SourceAgents.Count > 0)
        {
            query = query.Where(item => specification.SourceAgents.Contains(item.SourceAgent));
        }

        if (specification.Components.Count > 0)
        {
            query = query.Where(item => specification.Components.Contains(item.Component));
        }

        var matches = await query.ToListAsync(cancellationToken);

        return matches
            .Where(item => IsInTimeRange(item, specification.From, specification.To))
            .Where(item => specification.MaximumEvidenceAge is null ||
                item.CreatedAt >= DateTimeOffset.UtcNow - specification.MaximumEvidenceAge)
            .OrderByDescending(item => item.CreatedAt)
            .ThenBy(item => item.EvidenceId)
            .ToArray();
    }

    private static bool IsInTimeRange(
        EvidenceRecord evidence,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        if ((from is not null || to is not null) &&
            evidence.ObservedFrom is null &&
            evidence.ObservedTo is null)
        {
            return false;
        }

        if (from is not null && evidence.ObservedTo is not null && evidence.ObservedTo < from)
        {
            return false;
        }

        if (to is not null && evidence.ObservedFrom is not null && evidence.ObservedFrom > to)
        {
            return false;
        }

        return true;
    }
}
