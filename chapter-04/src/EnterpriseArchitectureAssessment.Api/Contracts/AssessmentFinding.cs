namespace EnterpriseArchitectureAssessment.Api.Contracts;
public sealed record AssessmentFinding(string Agent, string Area, string Finding, IReadOnlyList<string> Evidence,
    double Confidence, IReadOnlyList<string> Risks, string Recommendation, IReadOnlyList<string> OpenQuestions);
