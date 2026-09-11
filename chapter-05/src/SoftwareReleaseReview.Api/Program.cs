using SoftwareReleaseReview.Api.Agents;
using SoftwareReleaseReview.Api.Orchestrations;
using SoftwareReleaseReview.Api.Persistence;
using SoftwareReleaseReview.Api.Persistence.Repositories;
using SoftwareReleaseReview.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
var reviewsConnection = builder.Configuration.GetConnectionString("Reviews")
    ?? $"Data Source={Path.Combine(builder.Environment.ContentRootPath, "App_Data", "reviews.db")}";
if (reviewsConnection == "Data Source=App_Data/reviews.db")
    reviewsConnection = $"Data Source={Path.Combine(builder.Environment.ContentRootPath, "App_Data", "reviews.db")}";
builder.Services.AddDbContext<ReviewDbContext>(options => options.UseSqlite(reviewsConnection));
builder.Services.AddScoped<IReviewRunRepository, ReviewRunRepository>();
builder.Services.AddScoped<ReviewExecutionService>();
builder.Services.AddSingleton<ReleaseReviewAgentFactory>();
builder.Services.AddSingleton<SecurityAgent>();
builder.Services.AddSingleton<QualityAgent>();
builder.Services.AddSingleton<ArchitectureAgent>();
builder.Services.AddSingleton<ReleaseAgent>();
builder.Services.AddTransient<SequentialReview>();
builder.Services.AddTransient<ConcurrentReview>();
builder.Services.AddTransient<HandoffReview>();
builder.Services.AddTransient<GroupChatReview>();

var app = builder.Build();

Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "App_Data"));
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<ReviewDbContext>().Database.Migrate();
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.MapControllers();

app.Run();

public partial class Program;
