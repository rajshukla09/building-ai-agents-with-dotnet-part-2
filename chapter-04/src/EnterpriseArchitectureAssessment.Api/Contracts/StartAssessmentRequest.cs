namespace EnterpriseArchitectureAssessment.Api.Contracts;
public sealed record StartAssessmentRequest(string Objective, Guid? AssessmentId = null);
