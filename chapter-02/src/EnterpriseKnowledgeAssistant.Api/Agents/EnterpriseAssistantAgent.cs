using System.Diagnostics;
using System.Text.Json;
using EnterpriseKnowledgeAssistant.Api.Configuration;
using EnterpriseKnowledgeAssistant.Api.Contracts;
using EnterpriseKnowledgeAssistant.Api.Mcp.Client;
using EnterpriseKnowledgeAssistant.Api.Mcp.Models;
using Microsoft.Extensions.Options;

namespace EnterpriseKnowledgeAssistant.Api.Agents;

public sealed class EnterpriseAssistantAgent(
    IMcpClient mcpClient,
    IEnterpriseModelRunner model,
    IOptions<EnterpriseKnowledgeAssistantOptions> options,
    ILogger<EnterpriseAssistantAgent> logger) : IEnterpriseAssistantAgent
{
    public async Task<AssistantQueryResponse> QueryAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("A query is required.", nameof(query));
        }

        var catalog = await mcpClient.DiscoverToolsAsync(cancellationToken);
        var ledger = new InvocationLedger(options.Value.MaxToolCalls, logger);
        var modelTools = catalog
            .Select(descriptor => CreateModelTool(descriptor, catalog, ledger))
            .ToArray();

        string answer;
        try
        {
            answer = await model.RunAsync(query, modelTools, cancellationToken);
        }
        catch (McpToolCallLimitException)
        {
            ledger.MarkLimitReached();
            answer = "I stopped retrieving enterprise information because the configured MCP tool-call limit "
                + "was reached. Please narrow the request and try again.";
        }

        var invocations = ledger.Snapshot();
        logger.LogInformation(
            "Agent completed response after {InvocationCount} MCP invocations",
            invocations.Count);
        return new AssistantQueryResponse(answer, invocations, ledger.LimitReached);
    }

    private ModelTool CreateModelTool(
        McpToolDescriptor descriptor,
        IReadOnlyList<McpToolDescriptor> requestCatalog,
        InvocationLedger ledger)
    {
        var functionName = $"{descriptor.Server}__{descriptor.Name}";
        var description = $"MCP server: {descriptor.Server}. {descriptor.Description}";
        return new ModelTool(
            functionName,
            description,
            async (query, cancellationToken) =>
            {
                var sequence = ledger.Reserve();
                var stopwatch = Stopwatch.StartNew();
                var succeeded = false;
                string? resultText = null;
                string? error = null;
                var arguments = new Dictionary<string, JsonElement>
                {
                    ["query"] = JsonSerializer.SerializeToElement(query),
                };

                logger.LogInformation(
                    "Agent requested MCP tool {Server}/{Tool} as invocation {Sequence}",
                    descriptor.Server,
                    descriptor.Name,
                    sequence);
                try
                {
                    var result = await mcpClient.InvokeAsync(
                        new McpToolCall(descriptor.Server, descriptor.Name, arguments),
                        requestCatalog,
                        cancellationToken);
                    succeeded = result.Succeeded;
                    resultText = result.Content.GetRawText();
                    return resultText;
                }
                catch (Exception exception)
                {
                    error = exception.Message;
                    throw;
                }
                finally
                {
                    stopwatch.Stop();
                    ledger.Complete(new ToolInvocationResponse(
                        sequence,
                        descriptor.Server,
                        descriptor.Name,
                        stopwatch.ElapsedMilliseconds,
                        succeeded,
                        JsonSerializer.Serialize(arguments),
                        resultText,
                        error));
                }
            });
    }

    private sealed class InvocationLedger(int maxToolCalls, ILogger logger)
    {
        private readonly object _gate = new();
        private readonly List<ToolInvocationResponse> _completed = [];
        private int _reserved;
        public bool LimitReached { get; private set; }

        public int Reserve()
        {
            lock (_gate)
            {
                if (_reserved >= maxToolCalls)
                {
                    LimitReached = true;
                    logger.LogWarning(
                        "Maximum MCP tool calls ({MaxToolCalls}) reached; rejecting another model request",
                        maxToolCalls);
                    throw new McpToolCallLimitException(maxToolCalls);
                }
                return ++_reserved;
            }
        }

        public void Complete(ToolInvocationResponse invocation)
        {
            lock (_gate)
            {
                _completed.Add(invocation);
            }
        }

        public void MarkLimitReached()
        {
            lock (_gate)
            {
                LimitReached = true;
            }
        }

        public IReadOnlyList<ToolInvocationResponse> Snapshot()
        {
            lock (_gate)
            {
                return _completed.OrderBy(invocation => invocation.Sequence).ToArray();
            }
        }
    }
}

public sealed class McpToolCallLimitException(int limit)
    : InvalidOperationException($"The MCP tool-call limit of {limit} was reached.");
