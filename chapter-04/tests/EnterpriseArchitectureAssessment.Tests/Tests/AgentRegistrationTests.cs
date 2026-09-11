using EnterpriseArchitectureAssessment.Api.Agents;
using Xunit;
namespace EnterpriseArchitectureAssessment.Tests;
public sealed class AgentRegistrationTests
{
    [Fact]
    public void Six_focused_MAF_agent_types_are_separate()
    {
        Type[] types = [
            typeof(ArchitectureAgent), typeof(SecurityAgent), typeof(CostAgent), typeof(OperationsAgent),
            typeof(MigrationAgent), typeof(ResearchAgent)
        ];
        Assert.Equal(6, types.Select(x => x.Name).Distinct().Count());
    }
}
