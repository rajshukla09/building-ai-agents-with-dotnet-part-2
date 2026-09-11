using ClaimsReview.Api;
using ClaimsReview.Contracts;
using Microsoft.Extensions.Options;
using Xunit;
public sealed class Chapter02Tests
{
    static ClaimDraft Draft(decimal amount = 1000, string type = "VehicleDamage",
                            IReadOnlyList<string>? evidence = null,
                            DateOnly? date = null) => new("POL-1", "Anita", type, "Crash", amount,
                                                          date ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
                                                          evidence ?? ["photo.jpg"]);
    [Fact]
    public void Valid_claim_passes()
    {
        var v = new ClaimValidator();
        Assert.True(v.Validate(Draft(), DateOnly.FromDateTime(DateTime.UtcNow)).IsValid);
    }
    [Fact]
    public void Missing_policy_fails()
    {
        var v = new ClaimValidator();
        Assert.False(v.Validate(Draft() with { PolicyNumber = "" }, DateOnly.FromDateTime(DateTime.UtcNow)).IsValid);
    }
    [Fact]
    public void Invalid_amount_fails()
    {
        Assert.False(new ClaimValidator().Validate(Draft(0), DateOnly.FromDateTime(DateTime.UtcNow)).IsValid);
    }
    [Fact]
    public void Future_date_fails()
    {
        Assert.False(new ClaimValidator()
                         .Validate(Draft(date: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))),
                                   DateOnly.FromDateTime(DateTime.UtcNow))
                         .IsValid);
    }
    [Fact]
    public void Missing_vehicle_photo_fails()
    {
        Assert.False(new ClaimValidator()
                         .Validate(Draft(evidence: ["report.pdf"]), DateOnly.FromDateTime(DateTime.UtcNow))
                         .IsValid);
    }
    [Fact]
    public async Task Intake_agent_returns_draft()
    {
        var r = await new TestClaimIntakeAgent().ExecuteAsync(
            new(new("POL", "A", "VehicleDamage", "Desc", 10, DateOnly.FromDateTime(DateTime.UtcNow), ["photo"])));
        Assert.True(r.IsSuccess);
        Assert.Equal("POL", r.Value!.PolicyNumber);
    }
    [Fact]
    public async Task Intake_agent_honors_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new TestClaimIntakeAgent().ExecuteAsync(
                new(new("POL", "A", "VehicleDamage", "Description", 10,
                    DateOnly.FromDateTime(DateTime.UtcNow), ["photo"])),
                cancellation.Token));
    }
    [Fact]
    public void Risk_draft_maps_to_typed_assessment_without_granting_approval_authority()
    {
        var draft = new ClaimRiskAssessmentDraft(
            "High",
            85,
            ["High amount"],
            "Review the risk factors.",
            true);

        var assessment = RiskAssessmentAgent.ToAssessment(draft);

        Assert.NotNull(assessment);
        Assert.Equal(ClaimRiskLevel.High, assessment.RiskLevel);
        Assert.False(assessment.RequiresHumanReview);
    }
    [Fact]
    public void Invalid_risk_draft_is_rejected()
    {
        var draft = new ClaimRiskAssessmentDraft("Unknown", 50, ["Factor"], "Recommendation", null);

        Assert.Null(RiskAssessmentAgent.ToAssessment(draft));
    }
    [Fact]
    public void Approval_policy_skips_low()
    {
        var p = new HumanApprovalPolicy(Options.Create(new HumanApprovalOptions()));
        var c = new ClaimValidator().ToValidatedClaim(Draft());
        var a = new ClaimRiskAssessment(ClaimRiskLevel.Low, 10, [], "ok", false);
        Assert.False(p.Evaluate(c, a).IsRequired);
    }
    [Fact]
    public void Approval_policy_high_amount()
    {
        var p = new HumanApprovalPolicy(Options.Create(new HumanApprovalOptions()));
        var c = new ClaimValidator().ToValidatedClaim(Draft(85000));
        var a = new ClaimRiskAssessment(ClaimRiskLevel.Medium, 40, [], "review", true);
        Assert.True(p.Evaluate(c, a).IsRequired);
    }
    [Fact]
    public void Approval_policy_high_risk()
    {
        var p = new HumanApprovalPolicy(Options.Create(new HumanApprovalOptions()));
        var c = new ClaimValidator().ToValidatedClaim(Draft());
        var a = new ClaimRiskAssessment(ClaimRiskLevel.High, 80, [], "review", true);
        Assert.True(p.Evaluate(c, a).IsRequired);
    }
}
