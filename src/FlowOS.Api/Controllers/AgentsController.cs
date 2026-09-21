using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using FlowOS.Application.Commands;
using FlowOS.Agents.Events;
using FlowOS.Core.Interfaces;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Domain.Enums;

namespace FlowOS.Api.Controllers;

[ApiController]
[Route("api/agents")]
[Authorize]
public class AgentsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUser _currentUser;
    private readonly IAgentObservabilityQueryService _observability;
    private readonly TimeProvider _timeProvider;

    public AgentsController(
        IMediator mediator,
        ICurrentUser currentUser,
        IAgentObservabilityQueryService observability,
        TimeProvider timeProvider)
    {
        _mediator = mediator;
        _currentUser = currentUser;
        _observability = observability;
        _timeProvider = timeProvider;
    }

    [HttpPost("insight")]
    public async Task<IActionResult> PublishInsight(
        [FromBody] PublishInsightDto request,
        CancellationToken cancellationToken)
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

        var success = await _mediator.Send(command, cancellationToken);

        if (!success)
        {
            return NotFound(new { error = "Workflow instance not found or operation failed." });
        }

        return Ok(new { success = true, message = "Agent insight recorded." });
    }

    [HttpGet("{workflowInstanceId:guid}/history")]
    [HttpGet("instances/{workflowInstanceId:guid}/history")]
    public async Task<ActionResult<AgentExecutionHistoryDto>> GetExecutionHistory(
        Guid workflowInstanceId,
        [FromQuery] DateTimeOffset? fromUtc = null,
        [FromQuery] DateTimeOffset? toUtc = null,
        [FromQuery] string? status = null,
        [FromQuery] int limit = AgentObservabilityLimits.DefaultHistoryLimit,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Forbid();
        if (workflowInstanceId == Guid.Empty)
            return BadRequest(new { error = "workflowInstanceId is required." });
        if (limit is < 1 or > AgentObservabilityLimits.MaximumHistoryLimit)
        {
            return BadRequest(new
            {
                error = $"limit must be between 1 and {AgentObservabilityLimits.MaximumHistoryLimit}."
            });
        }
        if (!TryUtc(toUtc, "toUtc", out var normalizedTo, out var windowError))
        {
            if (toUtc.HasValue)
                return BadRequest(new { error = windowError });
            normalizedTo = _timeProvider.GetUtcNow().UtcDateTime;
        }
        if (!TryUtc(fromUtc, "fromUtc", out var normalizedFrom, out windowError))
        {
            if (fromUtc.HasValue)
                return BadRequest(new { error = windowError });
            normalizedFrom = normalizedTo.Subtract(AgentObservabilityLimits.DefaultHistoryWindow);
        }
        if (!TryStatus(status, out var parsedStatus))
            return BadRequest(new { error = "status is invalid." });
        if (!ValidateWindow(normalizedFrom, normalizedTo, out windowError))
            return BadRequest(new { error = windowError });

        try
        {
            return Ok(await _observability.GetExecutionHistoryAsync(
                new AgentExecutionHistoryRequest(
                    tenantId,
                    normalizedFrom,
                    normalizedTo,
                    workflowInstanceId,
                    parsedStatus,
                    limit),
                cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("metrics")]
    public async Task<ActionResult<AgentEvaluationMetricsDto>> GetEvaluationMetrics(
        [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? toUtc,
        CancellationToken cancellationToken)
    {
        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Forbid();
        if (!fromUtc.HasValue || !toUtc.HasValue)
            return BadRequest(new { error = "fromUtc and toUtc are required." });
        if (!TryUtc(fromUtc, "fromUtc", out var normalizedFrom, out var windowError) ||
            !TryUtc(toUtc, "toUtc", out var normalizedTo, out windowError) ||
            !ValidateWindow(normalizedFrom, normalizedTo, out windowError))
        {
            return BadRequest(new { error = windowError });
        }

        try
        {
            return Ok(await _observability.GetEvaluationMetricsAsync(
                new AgentEvaluationMetricsRequest(
                    tenantId,
                    normalizedFrom,
                    normalizedTo),
                cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { error = ex.Message });
        }
    }

    private static bool TryUtc(
        DateTimeOffset? value,
        string name,
        out DateTime normalized,
        out string? error)
    {
        normalized = default;
        error = null;
        if (!value.HasValue)
            return false;
        if (value.Value.Offset != TimeSpan.Zero)
        {
            error = $"{name} must use the UTC Z offset.";
            return false;
        }

        normalized = value.Value.UtcDateTime;
        return true;
    }

    private static bool ValidateWindow(
        DateTime fromUtc,
        DateTime toUtc,
        out string? error)
    {
        if (fromUtc >= toUtc)
        {
            error = "fromUtc must be earlier than toUtc.";
            return false;
        }
        if (toUtc - fromUtc > AgentObservabilityLimits.MaximumWindow)
        {
            error =
                $"The UTC window cannot exceed {AgentObservabilityLimits.MaximumWindow.TotalDays:0} days.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryStatus(
        string? value,
        out AgentExecutionStatus? status)
    {
        status = null;
        if (string.IsNullOrWhiteSpace(value))
            return true;
        if (!Enum.TryParse<AgentExecutionStatus>(
                value,
                ignoreCase: true,
                out var parsed) ||
            !Enum.IsDefined(parsed))
        {
            return false;
        }

        status = parsed;
        return true;
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
