using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using ObservableReleaseReview.Api.Contracts;
using Xunit;

namespace ObservableReleaseReview.Tests;

public sealed class ReleaseReviewsEndpointTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ReleaseReviewsEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("Release 4.8 contains documentation changes.", false, "Completed", "Safe to deploy")]
    [InlineData("Release 4.8 contains authentication and payment changes.", false, "Completed", "Deploy after high-risk review")]
    [InlineData("Release 4.8", true, "Failed", null)]
    public async Task Review_endpoint_returns_expected_workflow_result(
        string release,
        bool simulateFailure,
        string expectedStatus,
        string? expectedDecision)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/releases/review",
            new ReleaseReviewRequest(release, simulateFailure));

        Assert.True(
            response.IsSuccessStatusCode,
            await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<ReleaseReviewResponse>();

        Assert.NotNull(result);
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedDecision, result.FinalDecision);
        Assert.False(string.IsNullOrWhiteSpace(result.RunId));
    }
}
