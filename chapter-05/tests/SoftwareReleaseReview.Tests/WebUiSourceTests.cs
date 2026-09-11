using Xunit;

namespace SoftwareReleaseReview.Tests;

public sealed class WebUiSourceTests
{
    [Fact]
    public void Comparison_page_offers_all_four_api_patterns()
    {
        var source = WebSource("Pages", "Home.razor");

        Assert.Contains("new(\"sequential\"", source);
        Assert.Contains("new(\"concurrent\"", source);
        Assert.Contains("new(\"handoff\"", source);
        Assert.Contains("new(\"group-chat\"", source);
        Assert.Contains("api/reviews/{selected.Key}", source);
    }

    [Fact]
    public void Comparison_page_explains_each_execution_shape()
    {
        var source = WebSource("Pages", "Home.razor");

        Assert.Contains("each step depends on the previous result", source);
        Assert.Contains("independent tasks can run in parallel", source);
        Assert.Contains("transfer control to another", source);
        Assert.Contains("collaborate and discuss", source);
        Assert.Contains("FAN-OUT", source);
        Assert.Contains("HANDOFF", source);
    }

    [Fact]
    public void Magentic_is_reference_only_and_not_a_runnable_pattern()
    {
        var source = WebSource("Pages", "Home.razor");

        Assert.DoesNotContain("new(\"magentic\"", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Magentic orchestration is covered in Chapter 4", source);
    }

    private static string WebSource(params string[] parts) => File.ReadAllText(Path.Combine(
        [AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "SoftwareReleaseReview.Web", .. parts]));
}
