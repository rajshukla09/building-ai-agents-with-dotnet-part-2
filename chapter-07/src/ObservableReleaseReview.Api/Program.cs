using Microsoft.Agents.AI.Workflows.Observability;
using ObservableReleaseReview.Api;
using ObservableReleaseReview.Api.Observability;
using ObservableReleaseReview.Api.Workflows;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(builder.Configuration["WebOrigin"] ?? "https://localhost:7280")
        .AllowAnyHeader()
        .AllowAnyMethod()));
builder.Services.Configure<WorkflowTelemetryOptions>(options =>
    options.EnableSensitiveData = builder.Configuration.GetValue("WorkflowTelemetry:EnableSensitiveData", false));
builder.Services.AddSingleton<IReleaseReviewWorkflow, ReleaseReviewWorkflow>();
builder.Services.AddReleaseReviewObservability(builder.Configuration);

var app = builder.Build();
app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.Run();

public partial class Program;
