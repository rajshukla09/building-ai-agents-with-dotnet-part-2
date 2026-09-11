using EnterpriseArchitectureAssessment.Api.Mcp.Client;
namespace EnterpriseArchitectureAssessment.Api.Agents;
public sealed class ArchitectureAgent(IEnterpriseMcpClient mcp, IConfiguration configuration) : AssessmentAgentFactory(mcp, configuration)
{
    public override string Name => "ArchitectureAgent";
    protected override string Server => "architecture";
    protected override string Description => "Assesses coupling, scalability, boundaries, and event-driven suitability.";
    protected override string Instructions => "Analyze coupling, independent scaling needs, domain boundaries, consistency, and event suitability.";
}
