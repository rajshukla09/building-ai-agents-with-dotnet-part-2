namespace SoftwareReleaseReview.Api.Services;

public static class BusinessAgentNames
{
    private static readonly string[] Names =
        ["SecurityAgent", "QualityAgent", "ArchitectureAgent", "ReleaseAgent"];

    public static string? Normalize(string? executor)
    {
        if (string.IsNullOrWhiteSpace(executor)) return null;
        return Names.FirstOrDefault(name =>
            executor.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            executor.StartsWith(name + "_", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsReviewAgent(string? executor) => Normalize(executor) is not null;

    public static IReadOnlyList<string> OrderedFor(string pattern) => pattern switch
    {
        "Sequential" => Names,
        "Concurrent" => Names,
        "Concurrent reviews" => Names[..3],
        _ => []
    };
}
