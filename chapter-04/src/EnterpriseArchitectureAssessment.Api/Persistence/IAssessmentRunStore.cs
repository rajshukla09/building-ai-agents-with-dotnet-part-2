namespace EnterpriseArchitectureAssessment.Api.Persistence;
public interface IAssessmentRunStore
{
    AssessmentRun Create(Guid id, string objective);
    AssessmentRun? Get(Guid id);
    IReadOnlyList<AssessmentRun> List();
    void Save(AssessmentRun run);
}
