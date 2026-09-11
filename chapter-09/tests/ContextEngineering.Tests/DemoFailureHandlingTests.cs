using System.Net;
using System.Net.Http.Json;
using ContextEngineering.Api.Agents;
using ContextEngineering.Api.Models;
using ContextEngineering.Api.Observability;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ContextEngineering.Tests;

public sealed class DemoFailureHandlingTests
{
    [Fact]
    public async Task Missing_live_model_credentials_return_controlled_503_and_host_stays_alive()
    {
        await using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["AzureOpenAI:Endpoint"] = "https://configured.openai.azure.com",
            ["AzureOpenAI:ApiKey"] = "configure-with-user-secrets",
            ["AzureOpenAI:DeploymentName"] = "configured-deployment",
            ["AzureOpenAI:UseLiveModel"] = "true"
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/investigations/demo", null);
        var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>();
        var health = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("ApiKey", problem!.Detail);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task Language_model_failure_becomes_failed_investigation_and_host_stays_alive()
    {
        await using var factory = CreateFactory(
            new Dictionary<string, string?>
            {
                ["AzureOpenAI:UseLiveModel"] = "false"
            },
            services =>
            {
                services.RemoveAll<IInvestigationLanguageModel>();
                services.AddSingleton<IInvestigationLanguageModel, ThrowingLanguageModel>();
            });
        using var client = factory.CreateClient();

        var start = await client.PostAsync("/api/investigations/demo", null);
        var identity = await start.Content.ReadFromJsonAsync<StartResponse>();
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);

        InvestigationView? view = null;
        for (var attempt = 0; attempt < 80; attempt++)
        {
            view = await client.GetFromJsonAsync<InvestigationView>(
                $"/api/investigations/{identity!.IncidentId}");
            if (view?.Status == "Failed")
            {
                break;
            }

            await Task.Delay(100);
        }

        var health = await client.GetAsync("/health");
        Assert.Equal("Failed", view?.Status);
        Assert.Contains("Azure OpenAI", view?.FailureMessage);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        IReadOnlyDictionary<string, string?> configuration,
        Action<IServiceCollection>? configureServices = null)
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"chapter-09-failure-{Guid.NewGuid():N}.db");

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configurationBuilder) =>
                {
                    var values = new Dictionary<string, string?>(configuration)
                    {
                        ["ConnectionStrings:Evidence"] = $"Data Source={databasePath}"
                    };
                    configurationBuilder.AddInMemoryCollection(values);
                });

                if (configureServices is not null)
                {
                    builder.ConfigureServices(configureServices);
                }
            });
    }

    private sealed class ThrowingLanguageModel : IInvestigationLanguageModel
    {
        public Task<SpecialistFinding> GenerateFindingAsync(
            string agentName,
            EvidenceType evidenceType,
            string component,
            SpecialistRequest request,
            string deterministicDetail,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Simulated Azure OpenAI failure.");
        }

        public Task<FinalRecommendationView> GenerateRecommendationAsync(
            SupervisorExecutionState state,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Simulated Azure OpenAI failure.");
        }
    }

    private sealed record StartResponse(Guid IncidentId);

    private sealed record ProblemResponse(string? Title, string Detail, int? Status);
}
