using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContextEngineering.Api.Observability;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ContextEngineering.Tests;

public sealed class UiObservabilityTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"chapter-09-ui-{Guid.NewGuid():N}.db");
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private InvestigationView _completed = null!;

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:Evidence"] = $"Data Source={_databasePath}",
                        ["AzureOpenAI:UseLiveModel"] = "false"
                    });
                });
            });
        _client = _factory.CreateClient();

        var startResponse = await _client.PostAsync("/api/investigations/demo", null);
        Assert.Equal(HttpStatusCode.Accepted, startResponse.StatusCode);
        var started = await startResponse.Content.ReadFromJsonAsync<StartResponse>();
        Assert.NotNull(started);
        _completed = await WaitForCompletionAsync(started.IncidentId);
    }

    [Fact]
    public void Chapter_page_contains_teaching_workflow()
    {
        var chapterRoot = FindChapterRoot();
        var page = File.ReadAllText(Path.Combine(
            chapterRoot,
            "src",
            "ContextEngineering.Web",
            "Pages",
            "Home.razor"));

        Assert.Contains("@page \"/\"", page);
        Assert.Contains("Run sample investigation", page);
        Assert.Contains("Retrieval funnel", page);
        Assert.Contains("View exact safe Context Bundle", page);
        Assert.Contains("catch (HttpRequestException", page);
        Assert.Contains("_errorMessage", page);
    }

    [Fact]
    public void Web_client_uses_the_api_https_listener()
    {
        var chapterRoot = FindChapterRoot();
        var configurationPath = Path.Combine(
            chapterRoot,
            "src",
            "ContextEngineering.Web",
            "wwwroot",
            "appsettings.json");
        using var configuration = JsonDocument.Parse(File.ReadAllText(configurationPath));
        var apiBaseUrl = configuration.RootElement
            .GetProperty("ApiBaseUrl")
            .GetString();

        Assert.Equal("https://localhost:57179/", apiBaseUrl);
    }

    [Fact]
    public void Completed_projection_contains_compact_state_and_real_evidence_metrics()
    {
        Assert.Equal("Completed", _completed.Status);
        Assert.NotNull(_completed.SupervisorState);
        Assert.True(_completed.Metrics.StoredEvidenceRecords >= 50);
        Assert.True(_completed.Metrics.ApproximateStoredCharacters >
            _completed.Metrics.ApproximateCurrentContextCharacters);
        Assert.True(_completed.Metrics.ApproximateStoredCharacters >
            _completed.Metrics.ApproximateSupervisorStateCharacters);
    }

    [Fact]
    public void Retrieval_diagnostics_and_context_endpoint_projection_are_selected_only()
    {
        var execution = Assert.Single(
            _completed.AgentExecutions,
            item => item.Agent == "DatabaseAgent" && item.RetrievalIteration == 2);
        var bundle = Assert.IsType<ContextEngineering.Api.Models.ContextBundle>(
            execution.ContextBundle);
        var diagnostics = Assert.IsType<ContextEngineering.Api.Models.RetrievalDiagnostics>(
            bundle.Diagnostics);

        Assert.True(diagnostics.TotalIncidentEvidence > diagnostics.SelectedEvidenceCount);
        Assert.Equal(bundle.Evidence.Count, diagnostics.SelectedEvidenceCount);
        Assert.Equal(bundle.EvidenceIds.Order(), diagnostics.SelectedEvidenceIds.Order());
        Assert.True(bundle.Budget!.ApproximateCharacters <= bundle.Budget.MaximumCharacters);
        Assert.All(bundle.Evidence, item => Assert.Equal("orders-database", item.Component));
    }

    [Fact]
    public void Agent_details_show_adaptive_request_and_final_recommendation()
    {
        var firstDatabaseExecution = Assert.Single(
            _completed.AgentExecutions,
            item => item.Agent == "DatabaseAgent" && item.RetrievalIteration == 1);

        Assert.NotNull(firstDatabaseExecution.AdditionalContextRequired);
        Assert.Equal("orders-database", firstDatabaseExecution.AdditionalContextRequired.Component);
        Assert.NotNull(firstDatabaseExecution.NewEvidenceId);
        Assert.NotNull(_completed.FinalRecommendation);
        Assert.NotEmpty(_completed.FinalRecommendation.SupportingEvidenceIds);
        Assert.Contains(
            _completed.Timeline,
            item => item.EventType == "AdditionalContextRequested");
    }

    [Fact]
    public void Public_ui_contracts_do_not_expose_hidden_reasoning_or_prompts()
    {
        var propertyNames = typeof(InvestigationView)
            .Assembly
            .GetExportedTypes()
            .Where(type => type.Namespace == typeof(InvestigationView).Namespace)
            .SelectMany(type => type.GetProperties())
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(propertyNames, name =>
            name.Contains("ChainOfThought", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name =>
            name.Contains("SystemPrompt", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name =>
            name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<InvestigationView> WaitForCompletionAsync(Guid incidentId)
    {
        for (var attempt = 0; attempt < 80; attempt++)
        {
            var response = await _client.GetAsync($"/api/investigations/{incidentId}");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var view = await response.Content.ReadFromJsonAsync<InvestigationView>();
                if (view?.Status == "Completed")
                {
                    return view;
                }
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("The deterministic Chapter 9 demo did not complete.");
    }

    private static string FindChapterRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ContextEngineering.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Chapter 9 root.");
    }

    private sealed record StartResponse(Guid IncidentId);
}
