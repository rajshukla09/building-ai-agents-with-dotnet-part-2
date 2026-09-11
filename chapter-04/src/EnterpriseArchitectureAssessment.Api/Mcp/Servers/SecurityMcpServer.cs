using System.ComponentModel;
using ModelContextProtocol.Server;
namespace EnterpriseArchitectureAssessment.Api.Mcp.Servers;
[McpServerToolType]
public sealed class SecurityMcpServer
{
    [McpServerTool(Name="get_security_standards", ReadOnly=true, Idempotent=true, OpenWorld=false)]
    [Description("Required enterprise security standards.")]
    public static string GetSecurityStandards(string query) => "Zero trust, managed identity, encrypted events, and audit retention are required.";
    [McpServerTool(Name="get_authentication_model", ReadOnly=true, Idempotent=true, OpenWorld=false)]
    [Description("Current and target authentication model.")]
    public static string GetAuthenticationModel(string query) => "One application identity today; extracted services require workload identities.";
    [McpServerTool(Name="get_data_classification", ReadOnly=true, Idempotent=true, OpenWorld=false)]
    [Description("Data sensitivity and classification evidence.")]
    public static string GetDataClassification(string query) => "Payment token and customer data are restricted.";
}
