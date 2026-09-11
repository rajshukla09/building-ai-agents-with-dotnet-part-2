using EnterpriseIncidentInvestigator.Api.Contracts;

namespace EnterpriseIncidentInvestigator.Api.Orchestration;

public sealed class IncidentInvestigationContext(string originalIncident)
{
    private readonly List<AgentFinding> _findings = [];
    private readonly List<AgentInvocation> _invocations = [];
    public string OriginalIncident { get; } = originalIncident;
    public IReadOnlyList<AgentFinding> Findings => _findings;
    public IReadOnlyList<AgentInvocation> AgentInvocations => _invocations;
    public IReadOnlyCollection<string> CompletedAgents => _findings.Select(x => x.AgentName).Distinct().ToArray();
    public IEnumerable<EvidenceItem> Evidence => _findings.SelectMany(x => x.Evidence);
    public IEnumerable<string> CurrentHypotheses => _findings.Select(x => x.Summary);

    public void Add(AgentFinding finding, AgentInvocation invocation)
    { _findings.Add(finding); _invocations.Add(invocation); }
}
