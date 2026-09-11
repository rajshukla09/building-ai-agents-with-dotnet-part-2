using EnterpriseKnowledgeAssistant.Api.Agents;
using EnterpriseKnowledgeAssistant.Api.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace EnterpriseKnowledgeAssistant.Api.Controllers;

[ApiController, Route("api/assistant")]
public sealed class AssistantController(IEnterpriseAssistantAgent agent) : ControllerBase
{
    [HttpPost("query")]
    [ProducesResponseType<AssistantQueryResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AssistantQueryResponse>> Query(AssistantQueryRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query)) return ValidationProblem("Query is required.");
        return Ok(await agent.QueryAsync(request.Query, cancellationToken));
    }
}
