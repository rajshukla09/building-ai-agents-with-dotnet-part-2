using System.ComponentModel;
using ModelContextProtocol.Server;
namespace EnterpriseArchitectureAssessment.Api.Mcp.Servers;
[McpServerToolType]
public sealed class ResearchMcpServer
{
    [McpServerTool(Name="search_internal_docs", ReadOnly=true, Idempotent=true, OpenWorld=false)]
    [Description("Search internal architecture guidance.")]
    public static string SearchInternalDocs(string query) => "Use the smallest architecture satisfying a measurable need.";
    [McpServerTool(Name="get_previous_architecture_decisions", ReadOnly=true, Idempotent=true, OpenWorld=false)]
    [Description("Previous architecture decision records.")]
    public static string GetPreviousArchitectureDecisions(string query) => "ADR-42 rejected fleet decomposition without team readiness.";
    [McpServerTool(Name="get_reference_projects", ReadOnly=true, Idempotent=true, OpenWorld=false)]
    [Description("Comparable internal reference projects.")]
    public static string GetReferenceProjects(string query) => "Fulfilment succeeded with a modular monolith and two scale-out services.";
}
