using EnterpriseArchitectureAssessment.Api.Configuration;
using Xunit;
namespace EnterpriseArchitectureAssessment.Tests;
public sealed class AssessmentLimitsTests
{
    [Fact]
    public void Defaults_are_deterministic()
    {
        var x = new MagenticAssessmentOptions();
        Assert.Equal(12, x.MaxTurns);
        Assert.Equal(3, x.MaxStalls);
        Assert.Equal(2, x.MaxResets);
        Assert.Equal(10, x.MaxAgentInvocations);
        Assert.Equal(5, x.MaxToolCallsPerAgent);
    }
}
