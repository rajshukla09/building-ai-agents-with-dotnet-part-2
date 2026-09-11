using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EnterpriseArchitectureAssessment.Api.Persistence.Migrations;

[DbContext(typeof(AssessmentDbContext))]
[Migration("202608090001_InitialSqlite")]
public sealed class InitialSqlite : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE AssessmentRuns (
              Id TEXT NOT NULL PRIMARY KEY, Objective TEXT NOT NULL, Status TEXT NOT NULL,
              StartedAt TEXT NOT NULL, CompletedAt TEXT NULL, DurationMilliseconds INTEGER NULL,
              InitialPlanJson TEXT NULL, CurrentPlanJson TEXT NULL, AdditionalObservabilityJson TEXT NULL,
              CompletionReason TEXT NULL, FinalRecommendation TEXT NULL, LimitReached INTEGER NOT NULL,
              ModelDeployment TEXT NOT NULL, MaxTurns INTEGER NOT NULL, MaxAgentInvocations INTEGER NOT NULL,
              MaxToolCallsPerAgent INTEGER NOT NULL, InstructionVersion TEXT NOT NULL
            );
            CREATE INDEX IX_AssessmentRuns_StartedAt ON AssessmentRuns (StartedAt);
            CREATE TABLE PlanRevisions (
              Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, AssessmentRunId TEXT NOT NULL,
              FromVersion INTEGER NOT NULL, ToVersion INTEGER NOT NULL, Evidence TEXT NOT NULL,
              Change TEXT NOT NULL, At TEXT NOT NULL,
              FOREIGN KEY (AssessmentRunId) REFERENCES AssessmentRuns(Id) ON DELETE CASCADE
            );
            CREATE TABLE AgentInvocations (
              Id TEXT NOT NULL PRIMARY KEY, AssessmentRunId TEXT NOT NULL, Agent TEXT NOT NULL,
              Task TEXT NOT NULL, StartedAt TEXT NOT NULL, CompletedAt TEXT NULL, Result TEXT NULL,
              FOREIGN KEY (AssessmentRunId) REFERENCES AssessmentRuns(Id) ON DELETE CASCADE
            );
            CREATE TABLE ToolInvocations (
              Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, AssessmentRunId TEXT NOT NULL,
              AgentInvocationId TEXT NOT NULL, Agent TEXT NULL, Server TEXT NOT NULL, Tool TEXT NOT NULL,
              NormalizedArguments TEXT NOT NULL, CacheHit INTEGER NOT NULL, At TEXT NOT NULL,
              DurationMilliseconds INTEGER NULL, Success INTEGER NOT NULL, Error TEXT NULL,
              FOREIGN KEY (AssessmentRunId) REFERENCES AssessmentRuns(Id) ON DELETE CASCADE
            );
            CREATE TABLE AssessmentEvents (
              Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, AssessmentRunId TEXT NOT NULL,
              Sequence INTEGER NOT NULL, Type TEXT NOT NULL, At TEXT NOT NULL, Summary TEXT NOT NULL,
              Agent TEXT NULL, Tool TEXT NULL, PlanJson TEXT NULL,
              FOREIGN KEY (AssessmentRunId) REFERENCES AssessmentRuns(Id) ON DELETE CASCADE
            );
            CREATE TABLE ProgressEvaluations (
              Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, AssessmentRunId TEXT NOT NULL,
              Sequence INTEGER NOT NULL, Text TEXT NOT NULL,
              FOREIGN KEY (AssessmentRunId) REFERENCES AssessmentRuns(Id) ON DELETE CASCADE
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS ProgressEvaluations;
            DROP TABLE IF EXISTS AssessmentEvents;
            DROP TABLE IF EXISTS ToolInvocations;
            DROP TABLE IF EXISTS AgentInvocations;
            DROP TABLE IF EXISTS PlanRevisions;
            DROP TABLE IF EXISTS AssessmentRuns;
            """);
    }
}
