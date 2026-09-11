using System.Text.Json;
using EnterpriseKnowledgeAssistant.Api.Configuration;
using EnterpriseKnowledgeAssistant.Api.Mcp.Models;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EnterpriseKnowledgeAssistant.Api.Mcp.Client;

public sealed class EnterpriseMcpClient : IMcpClient, IAsyncDisposable
{
    private readonly McpOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<EnterpriseMcpClient> _logger;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private McpClient? _client;

    public EnterpriseMcpClient(
        IOptions<McpOptions> options,
        ILoggerFactory loggerFactory,
        ILogger<EnterpriseMcpClient> logger)
    {
        _options = options.Value;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public IReadOnlyList<McpServerDescriptor> Servers =>
    [
        new(_options.ServerName, "Chapter 2 enterprise tools", "MCP stdio", _client is not null),
    ];

    public async Task<IReadOnlyList<McpToolDescriptor>> DiscoverToolsAsync(
        CancellationToken cancellationToken = default)
    {
        var client = await GetClientAsync(cancellationToken);
        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
        var allowed = tools
            .Where(tool => tool.ProtocolTool.Annotations?.ReadOnlyHint == true)
            .Select(tool => new McpToolDescriptor(
                _options.ServerName,
                tool.Name,
                tool.Description ?? string.Empty,
                tool.ProtocolTool.InputSchema)
            {
                ClientTool = tool,
            })
            .ToArray();
        _logger.LogInformation(
            "Discovered {AllowedCount} read-only tools from MCP server {Server}; filtered {FilteredCount}",
            allowed.Length,
            _options.ServerName,
            tools.Count - allowed.Length);
        return allowed;
    }

    public async Task<McpToolResult> InvokeAsync(
        McpToolCall call,
        IReadOnlyList<McpToolDescriptor> requestCatalog,
        CancellationToken cancellationToken = default)
    {
        if (!call.Server.Equals(_options.ServerName, StringComparison.OrdinalIgnoreCase))
        {
            throw new McpToolValidationException($"MCP server '{call.Server}' is not connected.");
        }

        var descriptor = requestCatalog.SingleOrDefault(tool =>
            tool.Server.Equals(call.Server, StringComparison.OrdinalIgnoreCase)
            && tool.Name.Equals(call.Tool, StringComparison.OrdinalIgnoreCase))
            ?? throw new McpToolValidationException(
                $"MCP tool '{call.Server}/{call.Tool}' was not discovered for this request.");

        ValidateArguments(call.Arguments, descriptor.InputSchema);
        var arguments = call.Arguments.ToDictionary(
            pair => pair.Key,
            pair => (object?)pair.Value.Deserialize<object>());
        _logger.LogInformation(
            "Calling MCP server {Server} tool {Tool} with arguments {Arguments}",
            call.Server,
            call.Tool,
            JsonSerializer.Serialize(call.Arguments));

        try
        {
            CallToolResult result = await descriptor.ClientTool.CallAsync(
                arguments,
                cancellationToken: cancellationToken);
            var content = ReadContent(result);
            var succeeded = result.IsError != true;
            _logger.LogInformation(
                "MCP server {Server} tool {Tool} completed; Success={Succeeded}; Result={Result}",
                call.Server,
                call.Tool,
                succeeded,
                content.GetRawText());
            return new McpToolResult(call.Server, call.Tool, content, succeeded);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "MCP server {Server} tool {Tool} failed with arguments {Arguments}",
                call.Server,
                call.Tool,
                JsonSerializer.Serialize(call.Arguments));
            throw;
        }
    }

    private async Task<McpClient> GetClientAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            return _client;
        }

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (_client is not null)
            {
                return _client;
            }

            var assemblyPath = typeof(global::Program).Assembly.Location;
            var transport = new StdioClientTransport(
                new StdioClientTransportOptions
                {
                    Name = _options.ServerName,
                    Command = "dotnet",
                    Arguments = [assemblyPath, "--mcp-server"],
                    WorkingDirectory = Path.GetDirectoryName(assemblyPath),
                    InheritEnvironmentVariables = false,
                    EnvironmentVariables = StdioClientTransportOptions.GetDefaultEnvironmentVariables(),
                    StandardErrorLines = line => _logger.LogDebug("MCP server: {Line}", line),
                },
                _loggerFactory);
            _client = await McpClient.CreateAsync(
                transport,
                loggerFactory: _loggerFactory,
                cancellationToken: cancellationToken);
            _logger.LogInformation("Connected to MCP server {Server} over stdio", _options.ServerName);
            return _client;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private static void ValidateArguments(
        IReadOnlyDictionary<string, JsonElement> arguments,
        JsonElement inputSchema)
    {
        var properties = inputSchema.TryGetProperty("properties", out var propertyElement)
            ? propertyElement
            : default;
        HashSet<string> allowed = properties.ValueKind == JsonValueKind.Object
            ? properties.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal)
            : [];
        var required = inputSchema.TryGetProperty("required", out var requiredElement)
            ? requiredElement.EnumerateArray().Select(item => item.GetString()!).ToArray()
            : [];

        foreach (var name in required.Where(name => !arguments.ContainsKey(name)))
        {
            throw new McpToolValidationException($"Missing required MCP argument '{name}'.");
        }

        foreach (var argument in arguments)
        {
            if (!allowed.Contains(argument.Key))
            {
                throw new McpToolValidationException(
                    $"MCP argument '{argument.Key}' is not in the discovered input schema.");
            }

            var type = properties.GetProperty(argument.Key).TryGetProperty("type", out var typeElement)
                ? typeElement.GetString()
                : null;
            if (type == "string" && argument.Value.ValueKind != JsonValueKind.String)
            {
                throw new McpToolValidationException($"MCP argument '{argument.Key}' must be a string.");
            }
        }
    }

    private static JsonElement ReadContent(CallToolResult result)
    {
        if (result.StructuredContent is JsonElement structured)
        {
            return structured.Clone();
        }

        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "null";
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(text);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }
        _connectionLock.Dispose();
    }
}

public sealed class McpToolValidationException(string message) : InvalidOperationException(message);
