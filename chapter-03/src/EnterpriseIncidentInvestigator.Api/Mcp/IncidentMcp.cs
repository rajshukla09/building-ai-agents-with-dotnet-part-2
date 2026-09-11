using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Collections.Concurrent;
using System.Text.Json;

namespace EnterpriseIncidentInvestigator.Api.Mcp;

public sealed record McpToolDescriptor(string Name, string Description, McpClientTool ClientTool);
public sealed record McpServerDescriptor(string Name, string Description, string Transport = "MCP stdio");
public sealed record McpToolCall(string Server, string Tool, string Query);

public interface IIncidentMcpClient
{
    IReadOnlyList<McpServerDescriptor> Servers { get; }
    IReadOnlyList<McpToolDescriptor> DiscoverTools(string server);
    Task<string> InvokeAsync(McpToolCall call, CancellationToken cancellationToken);
}

public sealed class IncidentMcpClient : IIncidentMcpClient, IAsyncDisposable
{
    private static readonly McpServerDescriptor[] ServerCatalog =
    [
        new("deployment", "Deployment MCP Server"),
        new("logs", "Logs MCP Server"),
        new("metrics", "Metrics MCP Server"),
        new("database", "Database MCP Server")
    ];

    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<IncidentMcpClient> _logger;
    private readonly ConcurrentDictionary<string, McpClient> _clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<McpToolDescriptor>> _catalogs = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    public IncidentMcpClient(ILoggerFactory loggerFactory, ILogger<IncidentMcpClient> logger)
    {
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public IReadOnlyList<McpServerDescriptor> Servers => ServerCatalog;

    public IReadOnlyList<McpToolDescriptor> DiscoverTools(string server) =>
        DiscoverToolsAsync(server).GetAwaiter().GetResult();

    private async Task<IReadOnlyList<McpToolDescriptor>> DiscoverToolsAsync(
        string server, CancellationToken cancellationToken = default)
    {
        EnsureKnownServer(server);
        if (_catalogs.TryGetValue(server, out var existing)) return existing;

        var client = await GetClientAsync(server, cancellationToken);
        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
        var discovered = tools
            .Where(tool => tool.ProtocolTool.Annotations?.ReadOnlyHint == true)
            .Select(tool => new McpToolDescriptor(tool.Name, tool.Description ?? string.Empty, tool))
            .ToArray();
        _catalogs[server] = discovered;
        _logger.LogInformation("Discovered {Count} tools from MCP server {Server}", discovered.Length, server);
        return discovered;
    }

    public async Task<string> InvokeAsync(McpToolCall call, CancellationToken cancellationToken)
    {
        var catalog = await DiscoverToolsAsync(call.Server, cancellationToken);
        var tool = catalog.SingleOrDefault(candidate =>
            candidate.Name.Equals(call.Tool, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"MCP tool '{call.Server}/{call.Tool}' was not discovered from the server.");
        var arguments = new Dictionary<string, object?> { ["query"] = call.Query };
        _logger.LogInformation("Calling MCP server {Server} tool {Tool} with arguments {Arguments}",
            call.Server, call.Tool, JsonSerializer.Serialize(arguments));
        try
        {
            CallToolResult result = await tool.ClientTool.CallAsync(arguments, cancellationToken: cancellationToken);
            var content = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? string.Empty;
            _logger.LogInformation("MCP server {Server} tool {Tool} completed; Success={Success}; Result={Result}",
                call.Server, call.Tool, result.IsError != true, content);
            if (result.IsError == true) throw new InvalidOperationException(content);
            return content;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "MCP server {Server} tool {Tool} failed with arguments {Arguments}",
                call.Server, call.Tool, JsonSerializer.Serialize(arguments));
            throw;
        }
    }

    private async Task<McpClient> GetClientAsync(string server, CancellationToken cancellationToken)
    {
        if (_clients.TryGetValue(server, out var existing)) return existing;
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (_clients.TryGetValue(server, out existing)) return existing;
            var assemblyPath = typeof(global::Program).Assembly.Location;
            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = server,
                Command = "dotnet",
                Arguments = [assemblyPath, "--mcp-server", server],
                WorkingDirectory = Path.GetDirectoryName(assemblyPath),
                InheritEnvironmentVariables = false,
                EnvironmentVariables = StdioClientTransportOptions.GetDefaultEnvironmentVariables(),
                StandardErrorLines = line => _logger.LogDebug("MCP server {Server}: {Line}", server, line)
            }, _loggerFactory);
            var client = await McpClient.CreateAsync(transport, loggerFactory: _loggerFactory,
                cancellationToken: cancellationToken);
            _clients[server] = client;
            _logger.LogInformation("Connected to MCP server {Server} over stdio", server);
            return client;
        }
        finally { _connectionLock.Release(); }
    }

    private static void EnsureKnownServer(string server)
    {
        if (!ServerCatalog.Any(candidate => candidate.Name.Equals(server, StringComparison.OrdinalIgnoreCase)))
            throw new KeyNotFoundException($"MCP server '{server}' is not connected.");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients.Values) await client.DisposeAsync();
        _connectionLock.Dispose();
    }
}

public sealed class ToolBudget(int maximum)
{
    private int _calls;
    public int Reserve() => Interlocked.Increment(ref _calls) <= maximum
        ? _calls : throw new ToolCallLimitException(maximum);
}

public sealed class ToolCallLimitException(int limit) : InvalidOperationException
($"The per-agent MCP tool limit of {limit} was reached.");
