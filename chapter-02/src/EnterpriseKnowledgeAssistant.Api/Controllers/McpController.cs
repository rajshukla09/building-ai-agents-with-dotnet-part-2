using EnterpriseKnowledgeAssistant.Api.Mcp.Client;
using EnterpriseKnowledgeAssistant.Api.Mcp.Models;
using Microsoft.AspNetCore.Mvc;

namespace EnterpriseKnowledgeAssistant.Api.Controllers;

[ApiController, Route("api/mcp")]
public sealed class McpController(IMcpClient client) : ControllerBase
{
    [HttpGet("servers")]
    public ActionResult<IReadOnlyList<McpServerDescriptor>> Servers() => Ok(client.Servers);
    [HttpGet("tools")]
    public async Task<ActionResult<IReadOnlyList<McpToolDescriptor>>>
    Tools(CancellationToken cancellationToken) => Ok(await client.DiscoverToolsAsync(cancellationToken));
}
