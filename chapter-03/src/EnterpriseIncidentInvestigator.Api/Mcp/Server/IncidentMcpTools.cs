using System.ComponentModel;
using ModelContextProtocol.Server;

namespace EnterpriseIncidentInvestigator.Api.Mcp.Server;

[McpServerToolType]
public sealed class DeploymentMcpTools
{
    [McpServerTool(Name = "get_recent_deployments", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Recent release versions and deployment times.")]
    public static string GetRecentDeployments([Description("Incident-focused search text.")] string query) =>
        "08:00 UTC: checkout v4.8 deployed; order-api v2.3 deployed at 07:30 UTC.";

    [McpServerTool(Name = "get_release_changes", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Code and configuration changes in a release.")]
    public static string GetReleaseChanges([Description("Release or incident search text.")] string query) =>
        $"Release changes matching '{query}': v4.8 replaced the legacy payment token mapping with a nullable external-token mapping.";

    [McpServerTool(Name = "get_deployment_status", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Rollout status and health for a deployment.")]
    public static string GetDeploymentStatus([Description("Deployment or incident search text.")] string query) =>
        $"Deployment status matching '{query}': v4.8 rollout completed, but checkout health checks began failing at 08:05 UTC.";
}

[McpServerToolType]
public sealed class LogsMcpTools
{
    [McpServerTool(Name = "search_logs", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Search application logs using an incident-focused query.")]
    public static string SearchLogs([Description("Log search query.")] string query) =>
        $"Logs matching '{query}': 08:07 UTC NullReferenceException in PaymentTokenMapper on checkout v4.8; order-api also reports SQL timeout errors.";

    [McpServerTool(Name = "find_error_patterns", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Aggregate recurring exceptions and timeout patterns.")]
    public static string FindErrorPatterns([Description("Incident-focused search text.")] string query) =>
        "Patterns: PaymentTokenMapper NullReferenceException (checkout, 312 occurrences); SQL command timeout (order-api, 48 occurrences).";

    [McpServerTool(Name = "get_trace", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Retrieve a correlated request trace.")]
    public static string GetTrace([Description("Trace identifier or search text.")] string query) =>
        $"Trace matching '{query}': PaymentTokenMapper.Map failed before payment authorization; order-api trace shows 4.2s waiting on an order lookup.";
}

[McpServerToolType]
public sealed class MetricsMcpTools
{
    [McpServerTool(Name = "get_service_metrics", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Latency, throughput, saturation, and availability for a service.")]
    public static string GetServiceMetrics([Description("Service or incident search text.")] string query) =>
        $"Metrics for '{query}': checkout p95 1.8s, order-api p95 4.5s; CPU and memory normal.";

    [McpServerTool(Name = "get_error_rate", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Current error rates by service.")]
    public static string GetErrorRate([Description("Service or incident search text.")] string query) =>
        "Error rates: checkout 18%; order-api 7%; other services below 1%.";

    [McpServerTool(Name = "compare_metrics", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Compare service metrics across two time windows.")]
    public static string CompareMetrics([Description("Services and time windows to compare.")] string query) =>
        "Before/after: checkout failures rose from 0.4% to 18% at 08:05; order-api latency rose from 280ms to 4.5s.";
}

[McpServerToolType]
public sealed class DatabaseMcpTools
{
    [McpServerTool(Name = "get_db_health", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Database availability, saturation, and replication health.")]
    public static string GetDbHealth([Description("Database or incident search text.")] string query) =>
        "Primary database available; CPU 61%, connections 78%, replication healthy; order workload is degraded but not saturated.";

    [McpServerTool(Name = "get_slow_queries", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Slow and timed-out query samples.")]
    public static string GetSlowQueries([Description("Database or incident search text.")] string query) =>
        "Order lookup query timed out 48 times; p95 4.2s after a plan regression. Checkout has no slow queries.";

    [McpServerTool(Name = "get_connection_failures", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Connection failures, pool exhaustion, and network errors.")]
    public static string GetConnectionFailures([Description("Database or incident search text.")] string query) =>
        "Seven transient order-api connection failures; pool peaked at 78% with no exhaustion or failover.";
}
