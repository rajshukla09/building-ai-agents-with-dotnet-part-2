using EnterpriseArchitectureAssessment.Api.Mcp.Client;
namespace EnterpriseArchitectureAssessment.Api.Agents;
public sealed class CostAgent(IEnterpriseMcpClient mcp, IConfiguration configuration) : AssessmentAgentFactory(mcp, configuration)
{
    public override string Name => "CostAgent";
    protected override string Server => "cost";
    protected override string Description => "Assesses hosting, operating, and migration economics.";
    protected override string Instructions => "Compare current, full-target, selective-extraction, support, and migration costs.";
}
