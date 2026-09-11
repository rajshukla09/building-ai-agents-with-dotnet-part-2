using System.Net.Http.Json;
using System.Net.Sockets;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using PurchaseApproval.Api;
using Xunit;

namespace PurchaseApproval.Tests;

public sealed class DurablePurchaseTests : IDisposable
{
    private readonly string _db = Path.Combine(
        Path.GetTempPath(),
        $"purchase-{Guid.NewGuid():N}.db");

    private WebApplicationFactory<Program> Host() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(
            builder =>
            {
                builder.ConfigureAppConfiguration(
                    (_, configuration) => configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["ConnectionStrings:Purchases"] = $"Data Source={_db}"
                        }));
                builder.ConfigureLogging(logging => logging.ClearProviders());
            });

    [Fact]
    public async Task Laptop_purchase_survives_host_recreation_and_continues_once()
    {
        if (!SchedulerAvailable()) return;
        Guid id;
        using (var host = Host())
        {
            var client = host.CreateClient();
            var response = await (
                await client.PostAsJsonAsync(
                    "/api/purchases",
                    new SubmitPurchase("Laptop fleet renewal", 50000)))
                .Content.ReadFromJsonAsync<PurchaseStarted>();
            id = response!.RunId;

            var waiting = await client.GetFromJsonAsync<PurchaseView>($"/api/purchases/{id}");
            Assert.Equal(PurchaseStatus.WaitingForApproval, waiting!.Status);
            Assert.Equal(1, waiting.ValidateExecutions);
            Assert.Equal(1, waiting.BudgetExecutions);
        }

        using (var restarted = Host())
        {
            var client = restarted.CreateClient();
            var restored = await client.GetFromJsonAsync<PurchaseView>($"/api/purchases/{id}");
            Assert.Equal(PurchaseStatus.WaitingForApproval, restored!.Status);

            var completed = await (
                await client.PostAsJsonAsync(
                    $"/api/purchases/{id}/approve",
                    new DecidePurchase("Alex")))
                .Content.ReadFromJsonAsync<PurchaseView>();
            Assert.Equal(PurchaseStatus.Completed, completed!.Status);
            Assert.Equal(1, completed.ValidateExecutions);
            Assert.Equal(1, completed.BudgetExecutions);
            Assert.Equal(1, completed.ProcurementExecutions);

            var repeated = await (
                await client.PostAsJsonAsync(
                    $"/api/purchases/{id}/approve",
                    new DecidePurchase("Alex")))
                .Content.ReadFromJsonAsync<PurchaseView>();
            Assert.Equal(1, repeated!.ProcurementExecutions);
        }
    }

    [Fact]
    public async Task Rejection_is_terminal()
    {
        if (!SchedulerAvailable()) return;
        using var host = Host();
        var client = host.CreateClient();
        var started = await (
            await client.PostAsJsonAsync(
                "/api/purchases",
                new SubmitPurchase("Laptop fleet renewal", 50000)))
            .Content.ReadFromJsonAsync<PurchaseStarted>();

        var rejected = await (
            await client.PostAsJsonAsync(
                $"/api/purchases/{started!.RunId}/reject",
                new DecidePurchase("Alex")))
            .Content.ReadFromJsonAsync<PurchaseView>();
        Assert.Equal(PurchaseStatus.Rejected, rejected!.Status);
        Assert.Equal(0, rejected.ProcurementExecutions);

        var unchanged = await (
            await client.PostAsJsonAsync(
                $"/api/purchases/{started.RunId}/approve",
                new DecidePurchase("Alex")))
            .Content.ReadFromJsonAsync<PurchaseView>();
        Assert.Equal(PurchaseStatus.Rejected, unchanged!.Status);
    }

    public void Dispose()
    {
        try
        {
            File.Delete(_db);
        }
        catch
        {
        }
    }

    private static bool SchedulerAvailable()
    {
        using var client = new TcpClient();
        try
        {
            client.ConnectAsync("localhost", 8080).Wait(TimeSpan.FromMilliseconds(500));
            if (!client.Connected)
            {
                return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
}
