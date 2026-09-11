using EnterpriseArchitectureAssessment.Api.Mcp.Client;
namespace EnterpriseArchitectureAssessment.Api.Agents;
public sealed class OperationsAgent(IEnterpriseMcpClient mcp, IConfiguration configuration) : AssessmentAgentFactory(mcp, configuration)
{
    public override string Name => "OperationsAgent";
    protected override string Server => "operations";
    protected override string Description => "Assesses reliability, delivery, observability, and team readiness.";
    protected override string Instructions => "Analyze incidents, SLOs, deployments, on-call load, observability, and operational maturity.";
}
