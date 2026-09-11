using EnterpriseIncidentInvestigator.Api.Contracts;
using EnterpriseIncidentInvestigator.Api.Orchestration;

namespace EnterpriseIncidentInvestigator.Api.Agents;

public sealed record AgentDescriptor(string Name, string Description, string? McpServer = null, IReadOnlyList<string>? McpTools = null);

public interface ISpecializedAgent

{
    AgentDescriptor Descriptor { get; }
    Task<AgentFinding> InvestigateAsync(IncidentInvestigationContext context, string assignment, CancellationToken cancellationToken);
}

public interface IRootCauseAgent

{
    Task<RootCauseConclusion> SynthesizeAsync(IncidentInvestigationContext context, bool limitReached, CancellationToken cancellationToken);
}

public sealed record AgentDelegate(AgentDescriptor Descriptor, Func<string, string, CancellationToken, Task<AgentFinding>> InvokeAsync);

public interface ISupervisorModel
{
    Task<string> RunAsync(string incident, string context, IReadOnlyList<AgentDelegate> agents, CancellationToken cancellationToken);
}
