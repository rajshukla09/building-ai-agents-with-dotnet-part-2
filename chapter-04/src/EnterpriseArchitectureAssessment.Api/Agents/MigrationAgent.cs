using EnterpriseArchitectureAssessment.Api.Mcp.Client;
namespace EnterpriseArchitectureAssessment.Api.Agents;
public sealed class MigrationAgent(IEnterpriseMcpClient mcp, IConfiguration configuration) : AssessmentAgentFactory(mcp, configuration)
{
    public override string Name => "MigrationAgent";
    protected override string Server => "migration";
    protected override string Description => "Assesses sequencing, dependencies, reversibility, and transition risk.";
    protected override string Instructions => "Analyze dependency seams, strangler sequencing, rollback, release constraints, and migration risk.";
}
