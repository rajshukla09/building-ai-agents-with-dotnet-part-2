using ContextEngineering.Api.Models;

namespace ContextEngineering.Api.Agents;

public interface ISpecialistAgent
{
    string Name { get; }

    Task<SpecialistFinding> InvestigateAsync(
        SpecialistRequest request,
        CancellationToken cancellationToken);
}

public abstract class StatelessSpecialistAgent(
    IInvestigationLanguageModel? languageModel = null) : ISpecialistAgent
{
    public abstract string Name { get; }

    protected abstract EvidenceType FindingType { get; }

    protected abstract string DefaultComponent { get; }

    public Task<SpecialistFinding> InvestigateAsync(
        SpecialistRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (languageModel is not null)
        {
            return languageModel.GenerateFindingAsync(
                Name,
                FindingType,
                DefaultComponent,
                request,
                CreateDetail(request),
                cancellationToken);
        }

        var finding = CreateFinding(request);
        return Task.FromResult(finding);
    }

    protected virtual SpecialistFinding CreateFinding(SpecialistRequest request)
    {
        var priorEvidence = request.Context.Evidence.Count;
        var summary = $"{Name} completed '{request.CurrentTask}' using {priorEvidence} supplied evidence item(s).";
        var details = CreateDetail(request);

        return new SpecialistFinding(
            Name,
            FindingType,
            DefaultComponent,
            DateTimeOffset.UtcNow.AddMinutes(-30),
            DateTimeOffset.UtcNow,
            summary,
            details,
            $"The {DefaultComponent} path may contribute to post-deployment latency.",
            0.65);
    }

    private string CreateDetail(SpecialistRequest request)
    {
        return $"Incident {request.IncidentId}: checkout latency rose after deployment. " +
            $"Analysis was scoped to {DefaultComponent}; prior context count was {request.Context.Evidence.Count}.";
    }
}

public sealed class LogInvestigationAgent(
    IInvestigationLanguageModel? languageModel = null) : StatelessSpecialistAgent(languageModel)
{
    public override string Name => nameof(LogInvestigationAgent);

    protected override EvidenceType FindingType => EvidenceType.Log;

    protected override string DefaultComponent => "checkout-api";
}

public sealed class DeploymentAgent(
    IInvestigationLanguageModel? languageModel = null) : StatelessSpecialistAgent(languageModel)
{
    public override string Name => nameof(DeploymentAgent);

    protected override EvidenceType FindingType => EvidenceType.Deployment;

    protected override string DefaultComponent => "checkout-api";
}

public sealed class DatabaseAgent(
    IInvestigationLanguageModel? languageModel = null) : StatelessSpecialistAgent(languageModel)
{
    public override string Name => nameof(DatabaseAgent);

    protected override EvidenceType FindingType => EvidenceType.Database;

    protected override string DefaultComponent => "orders-database";
}

public sealed class InfrastructureTelemetryAgent(
    IInvestigationLanguageModel? languageModel = null) : StatelessSpecialistAgent(languageModel)
{
    public override string Name => nameof(InfrastructureTelemetryAgent);

    protected override EvidenceType FindingType => EvidenceType.Telemetry;

    protected override string DefaultComponent => "checkout-platform";
}

public sealed class ApplicationAgent(
    IInvestigationLanguageModel? languageModel = null) : StatelessSpecialistAgent(languageModel)
{
    public override string Name => nameof(ApplicationAgent);

    protected override EvidenceType FindingType => EvidenceType.Application;

    protected override string DefaultComponent => "checkout-api";
}
