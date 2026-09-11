using System.ComponentModel;
using ModelContextProtocol.Server;
namespace EnterpriseArchitectureAssessment.Api.Mcp.Servers;
[McpServerToolType]
public sealed class CostMcpServer
{
    [McpServerTool(Name="get_current_hosting_cost", ReadOnly=true, Idempotent=true, OpenWorld=false)]
    [Description("Current hosting and support cost evidence.")]
    public static string GetCurrentHostingCost(string query) => "Current hosting is $38k/month with 2.1 support FTE.";
    [McpServerTool(Name="estimate_target_architecture_cost", ReadOnly=true, Idempotent=true, OpenWorld=false)]
    [Description("Target architecture cost estimates.")]
    public static string EstimateTargetArchitectureCost(string query) => "Full target is $61k/month; selective extraction is $44k/month.";
}
