using System.Text.Json;
using EnterpriseArchitectureAssessment.Api.Configuration;
using EnterpriseArchitectureAssessment.Api.Mcp.Client;
using EnterpriseArchitectureAssessment.Api.Mcp.Models;
using EnterpriseArchitectureAssessment.Api.Orchestration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
namespace EnterpriseArchitectureAssessment.Tests;
public sealed class McpToolSelectionTests
{
    [Fact]
    public async Task Duplicate_call_in_one_invocation_is_cached_but_later_invocation_is_allowed()
    {
        await using var client = CreateClient();
        using var doc = JsonDocument.Parse("{\"query\":\"seasonal\"}");
        var id = Guid.NewGuid();
        var call = new McpToolCall(id, "architecture", "get_traffic_profile", doc.RootElement.Clone());
        Assert.False((await client.CallAsync(call)).FromCache);
        Assert.True((await client.CallAsync(call)).FromCache);
        client.CompleteInvocation(id);
        Assert.False((await client.CallAsync(call with { InvocationId = Guid.NewGuid() })).FromCache);
    }
    [Fact]
    public async Task Every_server_is_connected_over_transport_and_discovers_read_only_capabilities()
    {
        await using var client = CreateClient();
        foreach (var server in new[] { "architecture", "security", "cost", "operations", "migration", "research" })
        {
            var tools = client.Discover(server);
            Assert.NotEmpty(tools);
            Assert.All(tools, tool => Assert.True(tool.ReadOnly));
            Assert.All(tools, tool => Assert.Equal(server, tool.Server));
        }
    }
    private static EnterpriseMcpClient CreateClient() =>
        new(Options.Create(new MagenticAssessmentOptions()), Options.Create(new McpOptions()),
            new AssessmentEventWriter(), NullLoggerFactory.Instance,
            NullLogger<EnterpriseMcpClient>.Instance);
}
