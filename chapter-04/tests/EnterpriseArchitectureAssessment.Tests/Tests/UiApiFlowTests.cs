using System.Text.Json;
using Xunit;

namespace EnterpriseArchitectureAssessment.Tests;

public sealed class UiApiFlowTests
{
    [Fact]
    public void Web_api_base_url_matches_the_api_https_launch_url()
    {
        using var webSettings = JsonDocument.Parse(File.ReadAllText(Source(
            "src/EnterpriseArchitectureAssessment.Web/wwwroot/appsettings.json")));
        using var apiLaunchSettings = JsonDocument.Parse(File.ReadAllText(Source(
            "src/EnterpriseArchitectureAssessment.Api/Properties/launchSettings.json")));

        var configuredBaseUrl = webSettings.RootElement.GetProperty("ApiBaseUrl").GetString();
        var apiLaunchUrls = apiLaunchSettings.RootElement
            .GetProperty("profiles")
            .GetProperty("EnterpriseArchitectureAssessment.Api")
            .GetProperty("applicationUrl")
            .GetString()!
            .Split(';');

        Assert.Contains(configuredBaseUrl!.TrimEnd('/'), apiLaunchUrls.Select(url => url.TrimEnd('/')));
    }

    [Fact]
    public void Start_handler_posts_the_contract_and_surfaces_failures()
    {
        var source = File.ReadAllText(Source(
            "src/EnterpriseArchitectureAssessment.Web/Pages/Home.razor"));

        Assert.Contains("@onclick=\"RunAsync\"", source);
        Assert.Contains("new StartAssessmentRequest(objective)", source);
        Assert.Contains("api/assessments", source);
        Assert.Contains("response.IsSuccessStatusCode", source);
        Assert.Contains("role=\"alert\"", source);
        Assert.Contains("Logger.LogError", source);
    }

    private static string Source(string relative) => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "../../../../../",
        relative));
}
