using EnterpriseArchitectureAssessment.Api.Contracts;
namespace EnterpriseArchitectureAssessment.Api.Orchestration;
public interface IAssessmentService { Task<AssessmentRunResponse> StartAsync(StartAssessmentRequest request,CancellationToken cancellationToken); }
