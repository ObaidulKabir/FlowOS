using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using FlowOS.Application.Commands;
using FlowOS.Application.Common.Interfaces;

namespace FlowOS.Api.Controllers;

[ApiController]
[Route("api/agents")]
public class AgentsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUser _currentUser;

    public AgentsController(IMediator mediator, ICurrentUser currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    [HttpPost("insight")]
    public async Task<IActionResult> PublishInsight([FromBody] PublishInsightDto request)
    {
        var tenantId = _currentUser.TenantId;
        
        var command = new PublishAgentInsightCommand(
            tenantId,
            request.WorkflowInstanceId,
            request.AgentId,
            request.Insight,
            request.ContextObjective,
            request.CorrelationId
        );

        var success = await _mediator.Send(command);

        if (!success)
        {
            return NotFound(new { error = "Workflow instance not found or operation failed." });
        }

        return Ok(new { success = true, message = "Agent insight recorded." });
    }
}

public class PublishInsightDto
{
    public Guid WorkflowInstanceId { get; set; }
    public string AgentId { get; set; } = string.Empty;
    public string Insight { get; set; } = string.Empty;
    public string ContextObjective { get; set; } = string.Empty;
    public Guid? CorrelationId { get; set; }
}
