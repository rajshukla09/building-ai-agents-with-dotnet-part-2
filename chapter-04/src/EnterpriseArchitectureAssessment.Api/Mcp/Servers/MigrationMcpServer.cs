using System.ComponentModel;
using ModelContextProtocol.Server;
namespace EnterpriseArchitectureAssessment.Api.Mcp.Servers;
[McpServerToolType]
public sealed class MigrationMcpServer
{
    [McpServerTool(Name="get_dependency_map", ReadOnly=true, Idempotent=true, OpenWorld=false)]
    [Description("Migration dependency map.")]
    public static string GetDependencyMap(string query) => "Payments have a stable seam; order workflow shares 47 tables.";
    [McpServerTool(Name="estimate_migration_risk", ReadOnly=true, Idempotent=true, OpenWorld=false)]
    [Description("Migration risk estimates.")]
    public static string EstimateMigrationRisk(string query) => "Full decomposition is high risk; payment strangler extraction is medium-low.";
    [McpServerTool(Name="get_release_constraints", ReadOnly=true, Idempotent=true, OpenWorld=false)]
    [Description("Release and rollback constraints.")]
    public static string GetReleaseConstraints(string query) => "Peak freeze is Nov-Dec and rollback must preserve order consistency.";
}
