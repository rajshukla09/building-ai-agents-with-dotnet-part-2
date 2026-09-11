using System.ComponentModel;
using ModelContextProtocol.Server;
namespace EnterpriseArchitectureAssessment.Api.Mcp.Servers;
[McpServerToolType]
public sealed class ArchitectureMcpServer
{
    [McpServerTool(Name="get_current_architecture", ReadOnly=true, Idempotent=true, OpenWorld=false)]
    [Description("Current architecture and coupling evidence.")]
    public static string GetCurrentArchitecture(string query) => "8-year monolith; shared SQL database; highly coupled order workflow.";
    [McpServerTool(Name="get_service_dependencies", ReadOnly=true, Idempotent=true, OpenWorld=false)]
    [Description("Service and domain dependency evidence.")]
    public static string GetServiceDependencies(string query) => "Payments are separable; inventory and order state are strongly coupled.";
    [McpServerTool(Name="get_traffic_profile", ReadOnly=true, Idempotent=true, OpenWorld=false)]
    [Description("Workload traffic and saturation evidence.")]
    public static string GetTrafficProfile(string query) => "Peak is 3x normal; payment saturates first; other workloads have moderate pressure.";
}
