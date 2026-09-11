using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace SoftwareReleaseReview.Api.Persistence.Migrations;

[DbContext(typeof(ReviewDbContext))]
[Migration("202608090001_InitialReviewPersistence")]
public partial class InitialReviewPersistence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("ReviewRuns", table => new
        {
            Id = table.Column<Guid>(nullable: false), Pattern = table.Column<string>(maxLength: 32, nullable: false),
            ReleaseRequest = table.Column<string>(nullable: false), Status = table.Column<string>(maxLength: 32, nullable: false),
            StartedAt = table.Column<DateTimeOffset>(nullable: false), CompletedAt = table.Column<DateTimeOffset>(nullable: true),
            DurationMilliseconds = table.Column<long>(nullable: false), FinalDecision = table.Column<string>(nullable: true),
            Success = table.Column<bool>(nullable: false), Error = table.Column<string>(nullable: true)
        }, constraints: table => table.PrimaryKey("PK_ReviewRuns", x => x.Id));
        migrationBuilder.CreateTable("AgentExecutions", table => new
        {
            Id = table.Column<long>(nullable: false).Annotation("Sqlite:Autoincrement", true), ReviewRunId = table.Column<Guid>(nullable: false),
            Sequence = table.Column<int>(nullable: false), AgentName = table.Column<string>(nullable: false),
            StartedAt = table.Column<DateTimeOffset>(nullable: false), CompletedAt = table.Column<DateTimeOffset>(nullable: false),
            DurationMilliseconds = table.Column<long>(nullable: false), Status = table.Column<string>(nullable: false), Finding = table.Column<string>(nullable: true)
        }, constraints: table => { table.PrimaryKey("PK_AgentExecutions", x => x.Id); table.ForeignKey("FK_AgentExecutions_ReviewRuns_ReviewRunId", x => x.ReviewRunId, "ReviewRuns", "Id", onDelete: ReferentialAction.Cascade); });
        migrationBuilder.CreateTable("ReviewEvents", table => new
        {
            Id = table.Column<long>(nullable: false).Annotation("Sqlite:Autoincrement", true), ReviewRunId = table.Column<Guid>(nullable: false),
            Sequence = table.Column<int>(nullable: false), EventType = table.Column<string>(nullable: false), Agent = table.Column<string>(nullable: true),
            Timestamp = table.Column<DateTimeOffset>(nullable: false), Summary = table.Column<string>(nullable: true)
        }, constraints: table => { table.PrimaryKey("PK_ReviewEvents", x => x.Id); table.ForeignKey("FK_ReviewEvents_ReviewRuns_ReviewRunId", x => x.ReviewRunId, "ReviewRuns", "Id", onDelete: ReferentialAction.Cascade); });
        migrationBuilder.CreateIndex("IX_AgentExecutions_ReviewRunId_Sequence", "AgentExecutions", new[] { "ReviewRunId", "Sequence" }, unique: true);
        migrationBuilder.CreateIndex("IX_ReviewEvents_ReviewRunId_Sequence", "ReviewEvents", new[] { "ReviewRunId", "Sequence" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("AgentExecutions");
        migrationBuilder.DropTable("ReviewEvents");
        migrationBuilder.DropTable("ReviewRuns");
    }
}
