namespace EnterpriseArchitectureAssessment.Api.Contracts;
public sealed record AssessmentRecommendation(string Summary, IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Tradeoffs, double Confidence);
public sealed record AssessmentConflict(string Topic, IReadOnlyList<string> Positions, string Resolution);
