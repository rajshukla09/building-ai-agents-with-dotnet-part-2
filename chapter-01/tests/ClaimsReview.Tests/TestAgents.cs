using ClaimsReview.Api;
using ClaimsReview.Contracts;

internal sealed class TestClaimIntakeAgent : IClaimIntakeAgent
{
    public Task<AgentResult<ClaimDraft>> ExecuteAsync(
        ClaimIntakeAgentRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ClaimSubmissionRequest submission = request.Submission;
        var draft = new ClaimDraft(
            submission.PolicyNumber?.Trim(),
            submission.ClaimantName?.Trim(),
            submission.ClaimType?.Trim(),
            string.IsNullOrWhiteSpace(submission.Description) ? null : submission.Description.Trim(),
            submission.ClaimedAmount,
            submission.IncidentDate,
            submission.SupportingEvidence);
        return Task.FromResult(AgentResult<ClaimDraft>.Success(
            draft,
            nameof(TestClaimIntakeAgent)));
    }
}

internal sealed class TestRiskAssessmentAgent : IRiskAssessmentAgent
{
    public Task<AgentResult<ClaimRiskAssessment>> ExecuteAsync(
        RiskAssessmentAgentRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidatedClaim claim = request.Claim;
        int score = (int)Math.Min(100, claim.ClaimedAmount / 1000);
        var factors = new List<string>();

        if (claim.ClaimedAmount > 50_000)
        {
            factors.Add("High claimed amount");
        }

        if (claim.IncidentSummary.Contains("fraud", StringComparison.OrdinalIgnoreCase))
        {
            score = 85;
            factors.Add("Description contains fraud indicator");
        }

        if (request.ValidationWarnings.Count > 0)
        {
            score = Math.Min(100, score + 10);
            factors.AddRange(request.ValidationWarnings);
        }

        ClaimRiskLevel level = score >= 70
            ? ClaimRiskLevel.High
            : score >= 25
                ? ClaimRiskLevel.Medium
                : ClaimRiskLevel.Low;
        var assessment = new ClaimRiskAssessment(
            level,
            score,
            factors.Count == 0 ? ["Routine claim"] : factors,
            "Test risk assessment.",
            false);
        return Task.FromResult(AgentResult<ClaimRiskAssessment>.Success(
            assessment,
            nameof(TestRiskAssessmentAgent)));
    }
}
