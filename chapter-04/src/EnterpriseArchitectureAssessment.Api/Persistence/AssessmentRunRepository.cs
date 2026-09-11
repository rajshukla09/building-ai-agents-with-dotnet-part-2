using System.Text.Json;
using EnterpriseArchitectureAssessment.Api.Configuration;
using EnterpriseArchitectureAssessment.Api.Contracts;
using EnterpriseArchitectureAssessment.Api.Orchestration;
using EnterpriseArchitectureAssessment.Api.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EnterpriseArchitectureAssessment.Api.Persistence;

public sealed class AssessmentRunRepository(
    AssessmentDbContext db,
    IOptions<MagenticAssessmentOptions> options,
    IConfiguration configuration) : IAssessmentRunStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public AssessmentRun Create(Guid id, string objective)
    {
        var limits = options.Value;
        var run = new AssessmentRun
        {
            Id = id,
            Objective = objective,
            StartedAt = DateTimeOffset.UtcNow,
            Configuration = new AssessmentConfigurationSnapshot
            {
                ModelDeployment = configuration["AzureOpenAI:DeploymentName"] ?? "",
                MaxTurns = limits.MaxTurns,
                MaxAgentInvocations = limits.MaxAgentInvocations,
                MaxToolCallsPerAgent = limits.MaxToolCallsPerAgent,
                InstructionVersion = MagenticAssessmentOrchestrator.InstructionVersion
            }
        };
        Save(run);
        return run;
    }

    public AssessmentRun? Get(Guid id) => db.AssessmentRuns.AsNoTracking().Any(x => x.Id == id) ? Load(id) : null;

    public IReadOnlyList<AssessmentRun> List() => db.AssessmentRuns.AsNoTracking().Select(x => x.Id)
        .AsEnumerable().Select(Load).OrderByDescending(x => x.StartedAt).ToArray();

    public void Save(AssessmentRun run)
    {
        using var transaction = db.Database.BeginTransaction();
        var old = db.AssessmentRuns
            .Include(x => x.PlanRevisions).Include(x => x.AgentInvocations).Include(x => x.ToolInvocations)
            .Include(x => x.Events).Include(x => x.ProgressEvaluations)
            .SingleOrDefault(x => x.Id == run.Id);
        if (old is not null)
        {
            db.AssessmentRuns.Remove(old);
            db.SaveChanges();
            db.ChangeTracker.Clear();
        }
        db.AssessmentRuns.Add(ToEntity(run));
        db.SaveChanges();
        transaction.Commit();
        db.ChangeTracker.Clear();
    }

    private AssessmentRun Load(Guid id)
    {
        var entity = db.AssessmentRuns.AsNoTracking()
            .Include(x => x.PlanRevisions).Include(x => x.AgentInvocations).Include(x => x.ToolInvocations)
            .Include(x => x.Events).Include(x => x.ProgressEvaluations).Single(x => x.Id == id);
        return ToDomain(entity);
    }

    private static AssessmentRunEntity ToEntity(AssessmentRun run) => new()
    {
        Id = run.Id, Objective = run.Objective, Status = run.Status, StartedAt = run.StartedAt,
        CompletedAt = run.CompletedAt, DurationMilliseconds = run.DurationMilliseconds,
        InitialPlanJson = Serialize(run.InitialPlan), CurrentPlanJson = Serialize(run.CurrentPlan),
        AdditionalObservabilityJson = Serialize(new AdditionalObservability(run.CompletedTasks, run.OpenQuestions,
            run.Conflicts, run.Findings)),
        CompletionReason = run.CompletionReason, FinalRecommendation = run.FinalRecommendation,
        LimitReached = run.LimitReached, ModelDeployment = run.Configuration.ModelDeployment,
        MaxTurns = run.Configuration.MaxTurns, MaxAgentInvocations = run.Configuration.MaxAgentInvocations,
        MaxToolCallsPerAgent = run.Configuration.MaxToolCallsPerAgent,
        InstructionVersion = run.Configuration.InstructionVersion,
        PlanRevisions = run.PlanRevisions.Select(x => new PlanRevisionEntity
        {
            FromVersion = x.FromVersion, ToVersion = x.ToVersion, Evidence = x.Evidence,
            Change = x.Change, At = x.At
        }).ToList(),
        AgentInvocations = run.AgentInvocations.Select(x => new AgentInvocationEntity
        {
            Id = x.Id, Agent = x.Agent, Task = x.Task, StartedAt = x.StartedAt,
            CompletedAt = x.CompletedAt, Result = x.Result
        }).ToList(),
        ToolInvocations = run.ToolInvocations.Select(x => new ToolInvocationEntity
        {
            AgentInvocationId = x.AgentInvocationId, Agent = x.Agent, Server = x.Server,
            Tool = x.Tool, NormalizedArguments = x.NormalizedArguments, CacheHit = x.CacheHit,
            At = x.At, DurationMilliseconds = x.DurationMilliseconds, Success = x.Success,
            Error = x.Error
        }).ToList(),
        Events = run.Events.Select(x => new AssessmentEventEntity
        {
            Sequence = x.Sequence, Type = x.Type, At = x.At, Summary = x.Summary,
            Agent = x.Agent, Tool = x.Tool, PlanJson = x.PlanJson
        }).ToList(),
        ProgressEvaluations = run.ProgressEvaluations.Select((x, i) => new ProgressEvaluationEntity
        {
            Sequence = i, Text = x
        }).ToList()
    };

    private static AssessmentRun ToDomain(AssessmentRunEntity x)
    {
        var extra = Deserialize<AdditionalObservability>(x.AdditionalObservabilityJson) ?? new([], [], [], []);
        return new AssessmentRun {
            Id=x.Id, Objective=x.Objective, Status=x.Status, StartedAt=x.StartedAt, CompletedAt=x.CompletedAt,
            DurationMilliseconds=x.DurationMilliseconds, InitialPlan=Deserialize<AssessmentPlan>(x.InitialPlanJson),
            CurrentPlan=Deserialize<AssessmentPlan>(x.CurrentPlanJson), CompletionReason=x.CompletionReason,
            FinalRecommendation=x.FinalRecommendation, LimitReached=x.LimitReached,
            Configuration=new() { ModelDeployment=x.ModelDeployment, MaxTurns=x.MaxTurns, MaxAgentInvocations=x.MaxAgentInvocations, MaxToolCallsPerAgent=x.MaxToolCallsPerAgent, InstructionVersion=x.InstructionVersion },
            CompletedTasks=extra.CompletedTasks, OpenQuestions=extra.OpenQuestions, Conflicts=extra.Conflicts, Findings=extra.Findings,
            PlanRevisions=x.PlanRevisions.OrderBy(y=>y.At).Select(y=>new PlanRevision(y.FromVersion,y.ToVersion,y.Evidence,y.Change,y.At)).ToList(),
            AgentInvocations=x.AgentInvocations.OrderBy(y=>y.StartedAt).Select(y=>new AgentInvocation(y.Id,y.Agent,y.Task,y.StartedAt,y.CompletedAt,y.Result)).ToList(),
            ToolInvocations=x.ToolInvocations.OrderBy(y=>y.At).Select(y=>new ToolInvocation(y.AgentInvocationId,y.Server,y.Tool,y.NormalizedArguments,y.CacheHit,y.At,y.DurationMilliseconds,y.Success,y.Error,y.Agent)).ToList(),
            Events=x.Events.OrderBy(y=>y.Sequence).Select(y=>new AssessmentEvent(y.Sequence,y.Type,y.At,y.Summary,y.Agent,y.Tool,y.PlanJson)).ToList(),
            ProgressEvaluations=x.ProgressEvaluations.OrderBy(y=>y.Sequence).Select(y=>y.Text).ToList()
        };
    }
    private static string? Serialize<T>(T? value) => value is null ? null : JsonSerializer.Serialize(value, JsonOptions);
    private static T? Deserialize<T>(string? value) => value is null ? default : JsonSerializer.Deserialize<T>(value, JsonOptions);
    private sealed record AdditionalObservability(List<string> CompletedTasks, List<string> OpenQuestions,
        List<AssessmentConflict> Conflicts, List<AssessmentFinding> Findings);
}
