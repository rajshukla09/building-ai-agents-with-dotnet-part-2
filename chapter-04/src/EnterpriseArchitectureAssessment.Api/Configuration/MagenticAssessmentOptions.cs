namespace EnterpriseArchitectureAssessment.Api.Configuration;
public sealed class MagenticAssessmentOptions
{
    public const string SectionName = "MagenticAssessment";
    public int MaxTurns { get; set; } = 12;
    public int MaxStalls { get; set; } = 3;
    public int MaxResets { get; set; } = 2;
    public int MaxAgentInvocations { get; set; } = 10;
    public int MaxToolCallsPerAgent { get; set; } = 5;
}
