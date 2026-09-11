using Microsoft.EntityFrameworkCore;
using Microsoft.Agents.AI.DurableTask;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.Worker.AzureManaged;
using PurchaseApproval.Api;

var builder = WebApplication.CreateBuilder(args);
var connection = builder.Configuration.GetConnectionString("Purchases")
    ?? "Data Source=App_Data/purchases.db";
var scheduler = builder.Configuration.GetConnectionString("DurableTaskScheduler")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:DurableTaskScheduler must identify the Durable Task Scheduler.");
var purchaseWorkflow = PurchaseWorkflow.Create(connection, TimeProvider.System);

builder.Services.AddDbContextFactory<PurchasesDb>(options => options.UseSqlite(connection));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(purchaseWorkflow);
builder.Services.ConfigureDurableWorkflows(
    workflows => workflows.AddWorkflow(purchaseWorkflow.Definition),
    worker => worker.UseDurableTaskScheduler(scheduler),
    client => client.UseDurableTaskScheduler(scheduler));
builder.Services.AddSingleton<PurchaseService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
var dataSource = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connection).DataSource;
var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
if (directory is not null)
{
    Directory.CreateDirectory(directory);
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider
        .GetRequiredService<IDbContextFactory<PurchasesDb>>()
        .CreateDbContext();
    db.Database.EnsureCreated();
}

app.UseSwagger();
app.UseSwaggerUI();

var api = app.MapGroup("/api/purchases");
api.MapPost(
    "/",
    async (SubmitPurchase request, PurchaseService service, CancellationToken ct) =>
        Results.Created("/api/purchases", await service.Start(request, ct)));
api.MapGet(
    "/{runId:guid}",
    async (Guid runId, PurchaseService service, CancellationToken ct) =>
        (await service.Get(runId, ct)) is { } run ? Results.Ok(run) : Results.NotFound());
api.MapPost(
    "/{runId:guid}/approve",
    async (Guid runId, DecidePurchase command, PurchaseService service, CancellationToken ct) =>
        (await service.Decide(runId, true, command.Manager ?? "manager", ct)) is { } run
            ? Results.Ok(run)
            : Results.Conflict());
api.MapPost(
    "/{runId:guid}/reject",
    async (Guid runId, DecidePurchase command, PurchaseService service, CancellationToken ct) =>
        (await service.Decide(runId, false, command.Manager ?? "manager", ct)) is { } run
            ? Results.Ok(run)
            : Results.Conflict());

app.Run();

public partial class Program;
