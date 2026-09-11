using EnterpriseArchitectureAssessment.Api.Mcp.Client;
namespace EnterpriseArchitectureAssessment.Api.Agents;
public sealed class SecurityAgent(IEnterpriseMcpClient mcp, IConfiguration configuration) : AssessmentAgentFactory(mcp, configuration)
{
    public override string Name => "SecurityAgent";
    protected override string Server => "security";
    protected override string Description => "Assesses identity, data protection, trust boundaries, and compliance.";
    protected override string Instructions => "Analyze trust boundaries, identity, secrets, restricted data, and compliance controls.";
}
