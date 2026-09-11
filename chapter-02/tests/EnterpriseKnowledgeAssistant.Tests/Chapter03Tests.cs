using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnterpriseKnowledgeAssistant.Api.Agents;
using EnterpriseKnowledgeAssistant.Api.Configuration;
using EnterpriseKnowledgeAssistant.Api.Contracts;
using EnterpriseKnowledgeAssistant.Api.Mcp.Client;
using EnterpriseKnowledgeAssistant.Api.Mcp.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EnterpriseKnowledgeAssistant.Tests;

public sealed class Chapter03Tests
{
    [Fact]
    public async Task Real_mcp_client_discovers_tools_from_stdio_server()
    {
        await using var client = CreateClient();

        var tools = await client.DiscoverToolsAsync();

        Assert.Equal(
            ["find_customer", "search_documentation", "search_github"],
            tools.Select(tool => tool.Name).Order().ToArray());
        Assert.All(tools, tool =>
        {
            Assert.Equal("enterprise-knowledge", tool.Server);
            Assert.Equal("object", tool.InputSchema.GetProperty("type").GetString());
            Assert.True(tool.InputSchema.GetProperty("properties").TryGetProperty("query", out _));
        });
    }

    [Fact]
    public async Task Discovered_tool_is_invoked_through_mcp_transport()
    {
        await using var client = CreateClient();
        var catalog = await client.DiscoverToolsAsync();

        var result = await client.InvokeAsync(
            Call("find_customer", "Contoso"),
            catalog);

        Assert.True(result.Succeeded);
        var customer = Assert.Single(result.Content.EnumerateArray());
        Assert.Equal("Contoso", customer.GetProperty("name").GetString());
        Assert.Equal("Adele", customer.GetProperty("owner").GetString());
    }

    [Fact]
    public async Task Unknown_tool_is_rejected_before_protocol_invocation()
    {
        await using var client = CreateClient();
        var catalog = await client.DiscoverToolsAsync();

        await Assert.ThrowsAsync<McpToolValidationException>(() =>
            client.InvokeAsync(Call("delete_customer", "Contoso"), catalog));
    }

    [Fact]
    public async Task Discovered_mcp_tools_are_given_to_the_maf_agent_boundary()
    {
        await using var client = CreateClient();
        IReadOnlyList<ModelTool>? receivedTools = null;
        var model = new ScriptedModel(async tools =>
        {
            receivedTools = tools;
            return await tools.Single(tool => tool.Name.EndsWith("__find_customer", StringComparison.Ordinal))
                .InvokeAsync("Contoso", CancellationToken.None);
        });
        var agent = CreateAgent(client, model);

        var response = await agent.QueryAsync("Tell me about Contoso.");

        Assert.NotNull(receivedTools);
        Assert.Equal(3, receivedTools.Count);
        Assert.Contains(receivedTools, tool => tool.Name == "enterprise-knowledge__find_customer");
        var invocation = Assert.Single(response.Invocations);
        Assert.Equal("find_customer", invocation.Tool);
        Assert.True(invocation.Succeeded);
        Assert.Contains("Contoso", invocation.Arguments);
        Assert.Contains("Contoso", invocation.Result);
        Assert.Null(invocation.Error);
    }

    [Fact]
    public async Task Existing_assistant_endpoint_returns_valid_response()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEnterpriseModelRunner>();
                services.AddSingleton<IEnterpriseModelRunner>(new ScriptedModel(async tools =>
                {
                    var customer = await tools
                        .Single(tool => tool.Name.EndsWith("__find_customer", StringComparison.Ordinal))
                        .InvokeAsync("Contoso", CancellationToken.None);
                    return $"Customer response: {customer}";
                }));
            }));
        using var http = factory.CreateClient();

        var result = await http.PostAsJsonAsync(
            "/api/assistant/query",
            new AssistantQueryRequest("Tell me about Contoso."));
        var response = await result.Content.ReadFromJsonAsync<AssistantQueryResponse>();

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.NotNull(response);
        Assert.Contains("Contoso", response.Answer);
        Assert.Single(response.Invocations);
        Assert.True(response.Invocations[0].Succeeded);
    }

    [Fact]
    public async Task Discovered_schema_is_enforced_before_tool_call()
    {
        await using var client = CreateClient();
        var catalog = await client.DiscoverToolsAsync();
        var wrongType = new McpToolCall(
            "enterprise-knowledge",
            "find_customer",
            new Dictionary<string, JsonElement>
            {
                ["query"] = JsonSerializer.SerializeToElement(42),
            });

        await Assert.ThrowsAsync<McpToolValidationException>(() =>
            client.InvokeAsync(wrongType, catalog));
    }

    [Fact]
    public async Task Configured_tool_call_limit_stops_model_loop()
    {
        await using var client = CreateClient();
        var model = new ScriptedModel(async tools =>
        {
            for (var index = 0; index < 3; index++)
            {
                await tools[0].InvokeAsync("anything", CancellationToken.None);
            }
            return "unreachable";
        });

        var response = await CreateAgent(client, model, maxCalls: 2)
            .QueryAsync("Research broadly.");

        Assert.True(response.ToolCallLimitReached);
        Assert.Equal(2, response.Invocations.Count);
        Assert.Contains("tool-call limit", response.Answer);
    }

    private static EnterpriseMcpClient CreateClient() => new(
        Options.Create(new McpOptions { ServerName = "enterprise-knowledge" }),
        NullLoggerFactory.Instance,
        NullLogger<EnterpriseMcpClient>.Instance);

    private static EnterpriseAssistantAgent CreateAgent(
        IMcpClient client,
        IEnterpriseModelRunner model,
        int maxCalls = 5) => new(
        client,
        model,
        Options.Create(new EnterpriseKnowledgeAssistantOptions { MaxToolCalls = maxCalls }),
        NullLogger<EnterpriseAssistantAgent>.Instance);

    private static McpToolCall Call(string tool, string query) => new(
        "enterprise-knowledge",
        tool,
        new Dictionary<string, JsonElement>
        {
            ["query"] = JsonSerializer.SerializeToElement(query),
        });

    private sealed class ScriptedModel(Func<IReadOnlyList<ModelTool>, Task<string>> script)
        : IEnterpriseModelRunner
    {
        public Task<string> RunAsync(
            string query,
            IReadOnlyList<ModelTool> tools,
            CancellationToken cancellationToken) => script(tools);
    }
}
