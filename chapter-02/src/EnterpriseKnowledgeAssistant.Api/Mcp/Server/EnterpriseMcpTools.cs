using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace EnterpriseKnowledgeAssistant.Api.Mcp.Server;

[McpServerToolType]
public sealed class EnterpriseMcpTools
{
    private static readonly object[] Issues =
    [
        new { repository = "KnowledgeAssistant", number = 42,
            title = "Authentication fails for Contoso after token refresh", state = "open" },
        new { repository = "TravelPlanner", number = 18,
            title = "Document Authentication setup", state = "closed" },
        new { repository = "ClaimsReview", number = 7,
            title = "Add role-based authorization", state = "open" },
    ];

    private static readonly object[] Documentation =
    [
        new { title = "Azure OpenAI setup", area = "Platform",
            summary = "Create deployments, managed identity, networking, and quotas." },
        new { title = "Deployment Guide", area = "Engineering",
            summary = "Promotion, health checks, rollback, and ownership." },
        new { title = "Security Standards", area = "Security",
            summary = "Authentication, secrets, data classification, and audit controls." },
        new { title = "Architecture Overview", area = "Architecture",
            summary = "Service boundaries, event flows, and approved integration patterns." },
    ];

    private static readonly object[] Customers =
    [
        new { name = "Contoso", industry = "Manufacturing", owner = "Adele", status = "Active",
            latestActivity = "Proposal submitted 2026-07-28" },
        new { name = "Fabrikam", industry = "Retail", owner = "Diego", status = "Active",
            latestActivity = "Architecture workshop 2026-08-04" },
        new { name = "Northwind", industry = "Food Distribution", owner = "Megan", status = "Active",
            latestActivity = "Renewal due 2026-09-15" },
    ];

    [McpServerTool(Name = "search_github", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Search enterprise GitHub issue titles and repository names.")]
    public static string SearchGitHub(
        [Description("Words to find in issue titles or repository names.")] string query) =>
        JsonSerializer.Serialize(Issues.Where(item => HasAllTerms(item.ToString()!, query)));

    [McpServerTool(Name = "search_documentation", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Search internal architecture, deployment, platform, and security documentation.")]
    public static string SearchDocumentation(
        [Description("Words to find in documentation titles, areas, or summaries.")] string query) =>
        JsonSerializer.Serialize(Documentation.Where(item => HasAllTerms(item.ToString()!, query)));

    [McpServerTool(Name = "find_customer", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Find CRM customer records by customer name or account details.")]
    public static string FindCustomer(
        [Description("Customer name or account text to find.")] string query) =>
        JsonSerializer.Serialize(Customers.Where(item => HasAllTerms(item.ToString()!, query)));

    private static bool HasAllTerms(string value, string query) =>
        query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
}
