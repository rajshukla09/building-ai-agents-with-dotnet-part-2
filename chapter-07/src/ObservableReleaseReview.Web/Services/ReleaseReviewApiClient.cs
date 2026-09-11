using System.Net.Http.Json;
using ObservableReleaseReview.Web.Models;

namespace ObservableReleaseReview.Web.Services;

public sealed class ReleaseReviewApiClient(HttpClient httpClient)
{
    public async Task<ReleaseReviewResponse> ReviewAsync(
        ReleaseReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "api/releases/review",
            request,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ReleaseReviewResponse>(
            cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("The API returned an empty response.");
    }
}
