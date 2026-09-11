using EnterpriseArchitectureAssessment.Api.Mcp.Client;
namespace EnterpriseArchitectureAssessment.Api.Agents;
public sealed class ResearchAgent(IEnterpriseMcpClient mcp, IConfiguration configuration) : AssessmentAgentFactory(mcp, configuration)
{
    public override string Name => "ResearchAgent";
    protected override string Server => "research";
    protected override string Description => "Finds standards, prior decisions, and comparable internal projects.";
    protected override string Instructions => "Retrieve relevant enterprise standards, ADRs, and reference projects; treat precedent as evidence, not truth.";
}
