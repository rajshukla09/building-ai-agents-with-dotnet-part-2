using Azure;
using Azure.AI.OpenAI;
using EnterpriseIncidentInvestigator.Api.Configuration;
using EnterpriseIncidentInvestigator.Api.Contracts;
using EnterpriseIncidentInvestigator.Api.Mcp;
using EnterpriseIncidentInvestigator.Api.Observability;
using EnterpriseIncidentInvestigator.Api.Orchestration;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using System.Diagnostics;

namespace EnterpriseIncidentInvestigator.Api.Agents;

public abstract class McpSpecializedAgent(IIncidentMcpClient mcp, IOptions<InvestigationOptions> limits, IOptions<AzureOpenAIOptions> azure)
    : ISpecializedAgent
{
    protected abstract string Specialty { get; }
    protected abstract string SuggestedNextInvestigation { get; }
    protected abstract string Responsibility { get; }
    public AgentDescriptor Descriptor => new(GetType().Name, Responsibility, Specialty, mcp.DiscoverTools(Specialty).Select(x => x.Name).ToArray());

    public async Task<AgentFinding> InvestigateAsync(IncidentInvestigationContext context, string assignment, CancellationToken cancellationToken)
    {
        var telemetry = new List<ToolInvocation>();
        var budget = new ToolBudget(limits.Value.MaxToolCallsPerAgent);
        var functions = mcp.DiscoverTools(Specialty).Select(tool => AIFunctionFactory.Create(async (string query, CancellationToken ct) =>
        {
            var sequence = budget.Reserve(); var watch = Stopwatch.StartNew(); var succeeded = false; Exception? failure = null;
            using var toolActivity = InvestigationTelemetry.Source.StartActivity($"MCP {tool.Name}");
            toolActivity?.SetTag("mcp.server", Specialty);
            toolActivity?.SetTag("mcp.tool", tool.Name);
            toolActivity?.SetTag("mcp.arguments", query);
            toolActivity?.SetTag("agent.name", Descriptor.Name);
            try
            {
                var result = await mcp.InvokeAsync(new(Specialty, tool.Name, query), ct);
                succeeded = true;
                toolActivity?.SetTag("mcp.success", true);
                toolActivity?.SetTag("mcp.result", result);
                return result;
            }
            catch
            (Exception exception)
            {
                failure = exception;
                toolActivity?.SetTag("mcp.success", false);
                toolActivity?.SetTag("mcp.failure", exception.Message);
                toolActivity?.SetStatus(ActivityStatusCode.Error, exception.Message);
                throw;
            }
            finally
            { watch.Stop(); telemetry.Add(new(sequence, tool.Name, watch.ElapsedMilliseconds, succeeded, Specialty, failure?.Message)); }
        }, name: tool.Name, description: tool.Description)).ToArray();
        AIAgent agent = new AzureOpenAIClient(new Uri(azure.Value.Endpoint), new AzureKeyCredential(azure.Value.ApiKey)).GetChatClient(azure.Value.DeploymentName).AsAIAgent(new ChatClientAgentOptions
        {
            Name = Descriptor.Name,
            ChatOptions = new ChatOptions { Instructions = $"""
                You are {Descriptor.Name}, a specialized MAF agent reporting only to SupervisorAgent. {Descriptor.Description}
                Select whichever advertised MCP tools are necessary; do not follow a fixed tool sequence and never invent data.
                Return exactly these concise fields: Finding, Evidence, Confidence, SuggestedNextInvestigation.
                Make the next investigation concrete and evidence-driven. Never ask the user or supervisor a question, request permission, offer to continue, or say 'would you like me to' or 'should I proceed'.
                """, Tools = [.. functions] }
        });
        var prompt = $"Incident: {context.OriginalIncident}\nAssignment: {assignment}\nPrior findings: {string.Join(" | ", context.Findings.Select(x => x.Summary))}";
        var response = await agent.RunAsync(prompt, cancellationToken: cancellationToken);
        return new(Descriptor.Name, response.Text, [new(Descriptor.Name, response.Text)], 0.75, SuggestedNextInvestigation, telemetry);
    }
}

public sealed class DeploymentAgent(IIncidentMcpClient m, IOptions<InvestigationOptions> l, IOptions<AzureOpenAIOptions> a) : McpSpecializedAgent(m, l, a)
{
    protected override string Specialty => "deployment"; protected override string Responsibility => "Investigates only deployment history, release changes, versions, rollout status, and timing.";
    protected override string SuggestedNextInvestigation => "Correlate the identified release and rollout time with errors and service metrics.";
}

public sealed class LogAnalysisAgent(IIncidentMcpClient m, IOptions<InvestigationOptions> l, IOptions<AzureOpenAIOptions> a) : McpSpecializedAgent(m, l, a)
{
    protected override string Specialty => "logs";
    protected override string Responsibility => "Analyzes only application errors, exception patterns, request traces, and correlated failures.";
    protected override string SuggestedNextInvestigation => "Correlate the dominant error or trace with its deployment version and occurrence rate.";
}

public sealed class MetricsAgent(IIncidentMcpClient m, IOptions<InvestigationOptions> l, IOptions<AzureOpenAIOptions> a) : McpSpecializedAgent(m, l, a)
{
    protected override string Specialty => "metrics";
    protected override string Responsibility => "Investigates only latency, failure rate, throughput, resource usage, and before/after comparisons.";
    protected override string SuggestedNextInvestigation => "Investigate the subsystem whose latency or error-rate change aligns most closely with incident onset.";
}

public sealed class DatabaseAgent(IIncidentMcpClient m, IOptions<InvestigationOptions> l, IOptions<AzureOpenAIOptions> a) : McpSpecializedAgent(m, l, a)
{
    protected override string Specialty => "database";
    protected override string Responsibility => "Investigates only database health, slow queries, connection failures, timeouts, and query-plan regressions.";
    protected override string SuggestedNextInvestigation => "Compare the slow-query and connection-failure window with order-api latency and recent database changes.";
}
