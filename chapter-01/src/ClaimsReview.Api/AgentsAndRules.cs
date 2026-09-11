using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using ClaimsReview.Contracts;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
namespace ClaimsReview.Api;

public sealed class ClaimIntakeAgent : IClaimIntakeAgent
{
    internal const string Instructions = """
        Normalize an insurance claim submission into the ClaimDraft schema.
        Trim surrounding whitespace from text fields and preserve the submitter's meaning.
        Copy the claimed amount, incident date, and supporting evidence without changing them.
        Use null only when a source value is absent or blank.
        Do not validate the claim, assess risk, decide whether approval is required, approve or reject
        the claim, or invent missing claim details.
        """;

    private readonly AIAgent _agent;
    private readonly ILogger<ClaimIntakeAgent> _logger;

    public ClaimIntakeAgent(
        IOptions<AzureOpenAIOptions> options,
        ILogger<ClaimIntakeAgent> logger)
    {
        AzureOpenAIOptions settings = options.Value;
        _logger = logger;
        _agent = new AzureOpenAIClient(
                new Uri(settings.Endpoint),
                new AzureKeyCredential(settings.ApiKey))
            .GetChatClient(settings.DeploymentName)
            .AsAIAgent(new ChatClientAgentOptions
            {
                Name = nameof(ClaimIntakeAgent),
                ChatOptions = new ChatOptions
                {
                    Instructions = Instructions
                }
            });
    }

    public async Task<AgentResult<ClaimDraft>> ExecuteAsync(
        ClaimIntakeAgentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            AgentResponse<ClaimDraft> response = await _agent.RunAsync<ClaimDraft>(
                $"Normalize this claim submission:\n{JsonSerializer.Serialize(request.Submission)}",
                options: StructuredOutput.For<ClaimDraft>(),
                cancellationToken: cancellationToken);
            return AgentResult<ClaimDraft>.Success(response.Result, nameof(ClaimIntakeAgent));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Claim intake Agent returned invalid structured output.");
            return AgentResult<ClaimDraft>.Fail(
                nameof(ClaimIntakeAgent),
                new AgentFailure(
                    AgentFailureKind.StructuredOutput,
                    "intake-structured-output-invalid",
                    "The intake Agent returned an invalid ClaimDraft.",
                    exception.Path));
        }
        catch (RequestFailedException exception)
        {
            _logger.LogError(exception, "Azure OpenAI claim intake request failed.");
            return AgentResult<ClaimDraft>.Fail(
                nameof(ClaimIntakeAgent),
                AgentFailureFactory.FromRequestFailure("intake", exception));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Claim intake Agent failed.");
            return AgentResult<ClaimDraft>.Fail(
                nameof(ClaimIntakeAgent),
                new AgentFailure(
                    AgentFailureKind.Infrastructure,
                    "intake-agent-failed",
                    "The intake Agent could not process the claim."));
        }
    }
}

public sealed class RiskAssessmentAgent : IRiskAssessmentAgent
{
    internal const string Instructions = """
        Assess the risk signals in a validated insurance claim and return only the
        ClaimRiskAssessmentDraft schema.
        Base the score, level, factors, and recommendation only on the supplied validated claim and
        validation warnings. RiskLevel must be Low, Medium, or High. RiskScore must be from 0 to 100.
        Cite concise factors grounded in the supplied data. Do not approve or reject the claim and do
        not decide whether human approval is required. Set RequiresHumanReview to null; deterministic
        application policy owns that decision.
        """;

    private readonly AIAgent _agent;
    private readonly ILogger<RiskAssessmentAgent> _logger;

    public RiskAssessmentAgent(
        IOptions<AzureOpenAIOptions> options,
        ILogger<RiskAssessmentAgent> logger)
    {
        AzureOpenAIOptions settings = options.Value;
        _logger = logger;
        _agent = new AzureOpenAIClient(
                new Uri(settings.Endpoint),
                new AzureKeyCredential(settings.ApiKey))
            .GetChatClient(settings.DeploymentName)
            .AsAIAgent(new ChatClientAgentOptions
            {
                Name = nameof(RiskAssessmentAgent),
                ChatOptions = new ChatOptions
                {
                    Instructions = Instructions
                }
            });
    }

    public async Task<AgentResult<ClaimRiskAssessment>> ExecuteAsync(
        RiskAssessmentAgentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            string prompt = $"""
                Assess this validated claim:
                {JsonSerializer.Serialize(request.Claim)}

                Deterministic validation warnings:
                {JsonSerializer.Serialize(request.ValidationWarnings)}
                """;
            AgentResponse<ClaimRiskAssessmentDraft> response =
                await _agent.RunAsync<ClaimRiskAssessmentDraft>(
                    prompt,
                    options: StructuredOutput.For<ClaimRiskAssessmentDraft>(),
                    cancellationToken: cancellationToken);
            ClaimRiskAssessment? assessment = ToAssessment(response.Result);
            if (assessment is null)
            {
                return AgentResult<ClaimRiskAssessment>.Fail(
                    nameof(RiskAssessmentAgent),
                    new AgentFailure(
                        AgentFailureKind.Validation,
                        "risk-assessment-invalid",
                        "The risk Agent returned an invalid risk assessment."));
            }

            return AgentResult<ClaimRiskAssessment>.Success(assessment, nameof(RiskAssessmentAgent));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Risk Agent returned invalid structured output.");
            return AgentResult<ClaimRiskAssessment>.Fail(
                nameof(RiskAssessmentAgent),
                new AgentFailure(
                    AgentFailureKind.StructuredOutput,
                    "risk-structured-output-invalid",
                    "The risk Agent returned invalid structured output.",
                    exception.Path));
        }
        catch (RequestFailedException exception)
        {
            _logger.LogError(exception, "Azure OpenAI risk assessment request failed.");
            return AgentResult<ClaimRiskAssessment>.Fail(
                nameof(RiskAssessmentAgent),
                AgentFailureFactory.FromRequestFailure("risk", exception));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Risk assessment Agent failed.");
            return AgentResult<ClaimRiskAssessment>.Fail(
                nameof(RiskAssessmentAgent),
                new AgentFailure(
                    AgentFailureKind.Infrastructure,
                    "risk-agent-failed",
                    "The risk Agent could not assess the claim."));
        }
    }

    internal static ClaimRiskAssessment? ToAssessment(ClaimRiskAssessmentDraft draft)
    {
        if (!Enum.TryParse(draft.RiskLevel, true, out ClaimRiskLevel level)
            || draft.RiskScore is not (>= 0 and <= 100)
            || draft.RiskFactors is null
            || draft.RiskFactors.Count == 0
            || draft.RiskFactors.Any(string.IsNullOrWhiteSpace)
            || string.IsNullOrWhiteSpace(draft.Recommendation))
        {
            return null;
        }

        return new ClaimRiskAssessment(
            level,
            draft.RiskScore.Value,
            draft.RiskFactors,
            draft.Recommendation,
            false);
    }
}

internal static class StructuredOutput
{
    public static ChatClientAgentRunOptions For<T>() => new()
    {
        ChatOptions = new ChatOptions
        {
            ResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat.ForJsonSchema<T>()
        }
    };
}

internal static class AgentFailureFactory
{
    public static AgentFailure FromRequestFailure(string stage, RequestFailedException exception)
    {
        AgentFailureKind kind = exception.Status == 429
            ? AgentFailureKind.RateLimit
            : AgentFailureKind.Dependency;
        string code = exception.Status == 429
            ? $"{stage}-rate-limited"
            : $"{stage}-provider-failed";
        string message = exception.Status == 429
            ? "The Azure OpenAI request was rate limited."
            : "The Azure OpenAI request failed.";
        return new AgentFailure(kind, code, message);
    }
}
public sealed class ClaimValidator : IClaimValidator
{
    static readonly HashSet<string> Types = ["VehicleDamage", "Medical", "PropertyDamage"];
    public ClaimValidationResult Validate(ClaimDraft d, DateOnly today)
    {
        var e = new List<ClaimValidationError>();
        var w = new List<string>();
        if (string.IsNullOrWhiteSpace(d.PolicyNumber))
            e.Add(new("PolicyNumber", "Policy number is required."));
        if (string.IsNullOrWhiteSpace(d.ClaimantName))
            e.Add(new("ClaimantName", "Claimant name is required."));
        if (string.IsNullOrWhiteSpace(d.ClaimType) || !Types.Contains(d.ClaimType))
            e.Add(new("ClaimType", "Supported claim types are VehicleDamage, Medical, and PropertyDamage."));
        if (d.ClaimedAmount is null or <= 0)
            e.Add(new("ClaimedAmount", "Claimed amount must be greater than zero."));
        if (d.IncidentDate is null)
            e.Add(new("IncidentDate", "Incident date is required."));
        else if (d.IncidentDate > today)
            e.Add(new("IncidentDate", "Incident date cannot be in the future."));
        if (string.IsNullOrWhiteSpace(d.IncidentSummary))
            e.Add(new("Description", "Incident summary is required."));
        if (d.SupportingEvidence is null)
            e.Add(new("SupportingEvidence", "Supporting evidence collection is required."));
        else
        {
            if (d.ClaimType == "VehicleDamage" &&
                !d.SupportingEvidence.Any(x => x.Contains("photo", StringComparison.OrdinalIgnoreCase)))
                e.Add(new("SupportingEvidence", "Vehicle damage claims require at least one photo."));
            if (d.ClaimType == "Medical" &&
                !d.SupportingEvidence.Any(x => x.Contains("medical", StringComparison.OrdinalIgnoreCase) ||
                                               x.Contains("doctor", StringComparison.OrdinalIgnoreCase)))
                e.Add(new("SupportingEvidence", "Medical claims require at least one medical document."));
            if (d.ClaimType == "PropertyDamage" && d.SupportingEvidence.Count == 0)
                e.Add(new("SupportingEvidence", "Property damage claims require at least one evidence item."));
            if (d.SupportingEvidence.Count == 1)
                w.Add("Only one evidence item was submitted; reviewer may request additional corroboration.");
        }
        return new(e.Count == 0, e, w);
    }
    public ValidatedClaim ToValidatedClaim(ClaimDraft d) => new(d.PolicyNumber!, d.ClaimantName!, d.ClaimType!,
                                                                d.IncidentSummary!, d.ClaimedAmount!.Value,
                                                                d.IncidentDate!.Value, d.SupportingEvidence!);
}
public sealed class HumanApprovalPolicy(IOptions<HumanApprovalOptions> opt) : IHumanApprovalPolicy
{
    public HumanApprovalRequirement Evaluate(ValidatedClaim c, ClaimRiskAssessment r,
                                             IReadOnlyList<string>? warnings = null)
    {
        var o = opt.Value;
        if (!o.Enabled)
            return new(false, "Human approval is disabled.", []);
        var rules = new List<string>();
        if (c.ClaimedAmount > o.AmountThreshold)
            rules.Add($"Claimed amount exceeds {o.AmountThreshold:C0}.");
        if (r.RiskLevel == ClaimRiskLevel.High)
            rules.Add("Risk level is High.");
        if (r.RiskScore >= o.HighRiskScoreThreshold)
            rules.Add($"Risk score is {r.RiskScore}, at or above {o.HighRiskScoreThreshold}.");
        if (warnings?.Count > 0)
            rules.Add("Validation produced warnings requiring reviewer awareness.");
        if (rules.Count == 0)
            return new(false, "Low-risk claim can proceed without human approval.", []);
        return new(true, "Deterministic approval policy requires human review.", rules);
    }
}
