using System.Collections.Concurrent;
using System.Text.Json;
using System.Diagnostics;
using EnterpriseArchitectureAssessment.Api.Configuration;
using EnterpriseArchitectureAssessment.Api.Mcp.Models;
using EnterpriseArchitectureAssessment.Api.Orchestration;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
namespace EnterpriseArchitectureAssessment.Api.Mcp.Client;
public sealed class EnterpriseMcpClient(IOptions<MagenticAssessmentOptions> limits, IOptions<McpOptions> policy,
                                        AssessmentEventWriter events, ILoggerFactory loggerFactory,
                                        ILogger<EnterpriseMcpClient> logger)
    : IEnterpriseMcpClient, IAsyncDisposable
{
    private static readonly HashSet<string> Servers =
        ["architecture", "security", "cost", "operations", "migration", "research"];
    private readonly ConcurrentDictionary<string, McpClient> _clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<McpToolDescriptor>> _catalogs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, McpToolResult>> _invocations = new();
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    public IReadOnlyList<McpToolDescriptor> Discover(string server) =>
        DiscoverAsync(server).GetAwaiter().GetResult();
    private async Task<IReadOnlyList<McpToolDescriptor>> DiscoverAsync(string server,
                                                                       CancellationToken cancellationToken = default)
    {
        EnsureKnownServer(server);
        if (_catalogs.TryGetValue(server, out var cached)) return cached;
        var client = await GetClientAsync(server, cancellationToken);
        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
        var discovered = tools.Select(tool => new McpToolDescriptor(
                                  server, tool.Name, tool.Description ?? string.Empty,
                                  tool.ProtocolTool.Annotations?.ReadOnlyHint == true, tool)).ToArray();
        _catalogs[server] = discovered;
        logger.LogInformation("Discovered {Count} tools from MCP server {Server}: {Tools}",
                              discovered.Length, server, string.Join(", ", discovered.Select(x => x.Name)));
        return discovered;
    }
    public async Task<McpToolResult> CallAsync(McpToolCall call, CancellationToken cancellationToken = default)
    {
        var descriptor = (await DiscoverAsync(call.Server, cancellationToken)).SingleOrDefault(
                             x => x.Name.Equals(call.Tool, StringComparison.OrdinalIgnoreCase)) ??
                         throw new ArgumentException("Tool was not discovered from this MCP server.");
        if (policy.Value.ReadOnly && !descriptor.ReadOnly)
            throw new InvalidOperationException("Write MCP tools are disabled.");
        var arguments = Normalize(call.Arguments);
        var cache = _invocations.GetOrAdd(call.InvocationId,
                                          _ => new());
        var key = $"{call.Server}|{call.Tool}|{arguments}";
        if (cache.TryGetValue(key, out var cached))
        {
            events.ToolCalled(call.InvocationId, call.Server, call.Tool, arguments, true, 0, true);
            return cached with { FromCache = true };
        }
        if (cache.Count >= limits.Value.MaxToolCallsPerAgent)
            throw new InvalidOperationException("MCP tool-call limit reached.");
        var timer = Stopwatch.StartNew();
        try
        {
            var protocolArguments = call.Arguments.ValueKind == JsonValueKind.Object
                ? call.Arguments.EnumerateObject().ToDictionary(x => x.Name, x => (object?)x.Value.Clone())
                : throw new ArgumentException("MCP tool arguments must be a JSON object.");
            logger.LogInformation("Calling MCP server {Server} tool {Tool} with arguments {Arguments}",
                                  call.Server, call.Tool, arguments);
            CallToolResult response = await descriptor.ClientTool.CallAsync(protocolArguments,
                                                       cancellationToken: cancellationToken);
            var content = response.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? string.Empty;
            if (response.IsError == true) throw new InvalidOperationException(content);
            var result = new McpToolResult(content);
            timer.Stop();
            cache[key] = result;
            events.ToolCalled(call.InvocationId, call.Server, call.Tool, arguments, false,
                              timer.ElapsedMilliseconds, true);
            logger.LogInformation("MCP server {Server} tool {Tool} completed successfully: {Result}",
                                  call.Server, call.Tool, content);
            return result;
        }
        catch (Exception exception)
        {
            timer.Stop();
            events.ToolCalled(call.InvocationId, call.Server, call.Tool, arguments, false,
                              timer.ElapsedMilliseconds, false, exception.Message);
            logger.LogError(exception, "MCP server {Server} tool {Tool} failed with arguments {Arguments}",
                            call.Server, call.Tool, arguments);
            throw;
        }
    }
    public void CompleteInvocation(Guid invocationId) => _invocations.TryRemove(invocationId, out _);
    internal static string Normalize(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object
            ? "{" +
                  string.Join(',', value.EnumerateObject()
                                       .OrderBy(x => x.Name)
                                       .Select(x => JsonSerializer.Serialize(x.Name) + ":" + Normalize(x.Value))) +
                  "}"
        : value.ValueKind == JsonValueKind.Array
            ? "[" + string.Join(',', value.EnumerateArray().Select(Normalize)) + "]"
            : value.GetRawText();
    private async Task<McpClient> GetClientAsync(string server, CancellationToken cancellationToken)
    {
        if (_clients.TryGetValue(server, out var existing)) return existing;
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (_clients.TryGetValue(server, out existing)) return existing;
            var assemblyPath = typeof(global::Program).Assembly.Location;
            var transport = new StdioClientTransport(new StdioClientTransportOptions {
                Name = server, Command = "dotnet", Arguments = [assemblyPath, "--mcp-server", server],
                WorkingDirectory = Path.GetDirectoryName(assemblyPath), InheritEnvironmentVariables = false,
                EnvironmentVariables = StdioClientTransportOptions.GetDefaultEnvironmentVariables(),
                StandardErrorLines = line => logger.LogDebug("MCP server {Server}: {Line}", server, line)
            }, loggerFactory);
            var client = await McpClient.CreateAsync(transport, loggerFactory: loggerFactory,
                                                     cancellationToken: cancellationToken);
            _clients[server] = client;
            logger.LogInformation("Connected to MCP server {Server} over stdio", server);
            return client;
        }
        finally { _connectionLock.Release(); }
    }
    private static void EnsureKnownServer(string server)
    {
        if (!Servers.Contains(server)) throw new KeyNotFoundException($"MCP server '{server}' is not connected.");
    }
    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients.Values) await client.DisposeAsync();
        _connectionLock.Dispose();
    }
}
