using ClaimsReview.Api;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<AzureOpenAIOptions>()
    .Bind(builder.Configuration.GetSection(AzureOpenAIOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<HumanApprovalOptions>()
    .Bind(builder.Configuration.GetSection("HumanApproval"))
    .Validate(options => options.ApprovalTimeoutMinutes >= 1,
        "HumanApproval:ApprovalTimeoutMinutes must be at least one minute.")
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<ClaimsDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Claims") ?? "Data Source=claims-review.db"));
builder.Services.AddScoped<IClaimIntakeAgent, ClaimIntakeAgent>();
builder.Services.AddScoped<IRiskAssessmentAgent, RiskAssessmentAgent>();
builder.Services.AddScoped<IClaimValidator, ClaimValidator>();
builder.Services.AddScoped<IHumanApprovalPolicy, HumanApprovalPolicy>();
builder.Services.AddScoped<IClaimApprovalStore, EfClaimApprovalStore>();
builder.Services.AddScoped<ClaimIntakeAgentExecutor>();
builder.Services.AddScoped<ClaimValidationExecutor>();
builder.Services.AddScoped<RiskAssessmentAgentExecutor>();
builder.Services.AddScoped<HumanApprovalRequestExecutor>();
builder.Services.AddScoped<ClaimDecisionExecutor>();
builder.Services.AddScoped<ClaimReviewWorkflow>();
builder.Services.AddScoped<IClaimWorkflowService, ClaimWorkflowService>();
builder.Services.AddSingleton<IClaimWorkflowQueue, ClaimWorkflowQueue>();
builder.Services.AddHostedService<ClaimWorkflowBackgroundService>();
builder.Services.AddHostedService<ApprovalExpiryService>();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.AllowAnyHeader().AllowAnyMethod().AllowCredentials().SetIsOriginAllowed(_ => true)));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();
    await db.Database.EnsureCreatedAsync();
    await EnsureNativeApprovalColumnsAsync(db);
}

app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.MapHub<Microsoft.AspNetCore.SignalR.Hub>("/hubs/claim-workflows");
app.Run();

static async Task EnsureNativeApprovalColumnsAsync(ClaimsDbContext db)
{
    var connection = db.Database.GetDbConnection();
    await connection.OpenAsync();
    var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    await using (var command = connection.CreateCommand())
    {
        command.CommandText = "PRAGMA table_info('ClaimApprovals')";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }
    }

    foreach (var definition in new[] { "MafRequestId TEXT NOT NULL DEFAULT ''" })
    {
        var name = definition.Split(' ', 2)[0];
        if (!columns.Contains(name))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"ALTER TABLE ClaimApprovals ADD COLUMN {definition}";
            await command.ExecuteNonQueryAsync();
        }
    }
}

public partial class Program;
