using EnterpriseIncidentInvestigator.Api.Configuration;
using EnterpriseIncidentInvestigator.Api.Contracts;
using EnterpriseIncidentInvestigator.Api.Observability;
using EnterpriseIncidentInvestigator.Api.Orchestration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace EnterpriseIncidentInvestigator.Api.Controllers;

[ApiController, Route("api/incidents")]
public sealed class IncidentsController(IIncidentInvestigator investigator, IInvestigationEventStore events,
                                        IOptions<InvestigationOptions> options)
    : ControllerBase
{
    [HttpPost("investigate")]
    [ProducesResponseType<InvestigationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<InvestigationResponse>> Investigate(InvestigateIncidentRequest request,
                                                                       CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(request.Incident)
            ? BadRequest("Incident is required.")
            : Ok(await investigator.InvestigateAsync(request.Incident, request.RunId, cancellationToken));

    [HttpGet("runs")]
    public Task<IReadOnlyList<InvestigationRunSummary>>
    Runs(CancellationToken cancellationToken) => events.ListAsync(cancellationToken);

    [HttpGet("runs/{runId:guid}")]
    public async Task<ActionResult<InvestigationRunView>> Run(Guid runId, CancellationToken cancellationToken)
    {
        var run = await events.GetAsync(runId, options.Value.MaxAgentInvocations, options.Value.MaxToolCallsPerAgent,
                                        cancellationToken);
        return run is null ? NotFound() : Ok(run);
    }

    [HttpGet("runs/{runId:guid}/events")]
    public async Task<ActionResult<IReadOnlyList<InvestigationEventView>>>
    RunEvents(Guid runId, CancellationToken cancellationToken)
    {
        var run = await events.GetAsync(runId, options.Value.MaxAgentInvocations, options.Value.MaxToolCallsPerAgent,
                                        cancellationToken);
        return run is null ? NotFound() : Ok(run.Events);
    }
}
