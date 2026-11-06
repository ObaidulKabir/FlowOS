using System;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Application.Commands;
using FlowOS.Application.Queries;
using FlowOS.Core.Interfaces;
using FlowOS.API.Filters;
using MediatR;
using FlowOS.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace FlowOS.Api.Controllers;

[ApiController]
[Route("api/workflows")]
[Authorize]
public class WorkflowsController : ControllerBase
{
    private readonly ICurrentUser _currentUser;
    private readonly IMediator _mediator;
    private readonly ILogger<WorkflowsController> _logger;

    public WorkflowsController(ICurrentUser currentUser, IMediator mediator, ILogger<WorkflowsController> logger)
    {
        _currentUser = currentUser;
        _mediator = mediator;
        _logger = logger;
    }

    [HttpPost("start")]
    [RequireRuntimePlan]
    public async Task<IActionResult> Start([FromBody] StartWorkflowCommand command)
    {
        if (_currentUser.TenantId != Guid.Empty && command.TenantId != _currentUser.TenantId)
        {
             command = command with { TenantId = _currentUser.TenantId };
        }

        if (!FlowOS.Api.Http.IdempotencyKeyHeader.TryResolve(Request, command.IdempotencyKey, out var idempotencyKey, out var keyError))
            return BadRequest(new ProblemDetails { Title = "Invalid Idempotency-Key", Detail = keyError, Status = 400 });
        command = command with { IdempotencyKey = idempotencyKey };

        var result = await _mediator.Send(command);

        // Integration contract (CR-10): return the pinned definition and the caller's references, so a
        // client can store what it started without a second call. A replayed request returns the same.
        var summary = await _mediator.Send(new GetWorkflowByIdQuery { Id = result, TenantId = command.TenantId });
        return Ok(new
        {
            WorkflowInstanceId = result,
            WorkflowDefinitionId = summary?.DefinitionId,
            WorkflowVersion = summary?.Version,
            CorrelationId = summary?.CorrelationId,
            ExternalSourceSystem = summary?.ExternalSourceSystem,
            ExternalReference = summary?.ExternalReference,
            Status = summary?.Status,
            Warning = CtxEmptyDataWarning(command.Payload, command.ContextBindingId, command.ContextType)
        });
    }

    /// <summary>
    /// Tenant integration finding N1 (MCP parity): a payload only reaches the agent's composed
    /// DecisionPacket.Data when the instance is started through an activated context binding
    /// (ContextBindingId/ContextType) - the legacy selectors never apply inputMapping, so the payload
    /// is silently dropped. Warns (CTX-EMPTY-DATA) rather than failing, since the workflow may genuinely
    /// have no agent step that needs business context.
    /// </summary>
    private static string? CtxEmptyDataWarning(object? payload, Guid? contextBindingId, string? contextType)
    {
        if (contextBindingId.HasValue || !string.IsNullOrWhiteSpace(contextType))
            return null;

        if (!HasMeaningfulPayload(payload))
            return null;

        return "CTX-EMPTY-DATA: a payload was supplied but no ContextBindingId/ContextType was used to " +
               "start this instance, so the payload is not mapped into canonical context - any agent " +
               "step's composed Data will be empty. Start via an activated context binding " +
               "(ContextBindingId or ContextType) whose definition.inputMapping projects this payload, " +
               "or ignore this warning if no step on this workflow needs business context.";
    }

    private static bool HasMeaningfulPayload(object? payload)
    {
        if (payload == null) return false;
        if (payload is System.Text.Json.JsonElement element)
        {
            return element.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Undefined => false,
                System.Text.Json.JsonValueKind.Null => false,
                System.Text.Json.JsonValueKind.Object => element.EnumerateObject().Any(),
                System.Text.Json.JsonValueKind.Array => element.EnumerateArray().Any(),
                _ => true
            };
        }
        return true;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] FlowOS.Workflows.Enums.WorkflowInstanceStatus? status,
        [FromQuery] string? sourceSystem = null,
        [FromQuery] string? externalReference = null,
        [FromQuery] Guid? tenantId = null,
        [FromQuery] string? scope = null,
        [FromQuery] int? limit = null)
    {
        if (_currentUser.TenantId == Guid.Empty) return Unauthorized();

        // Platform administrators can ask for the newest instances of every tenant (scope=all).
        // Guid.Empty means "all tenants" to the query; it is only ever set here, after the admin check.
        var allTenants = AdminTenantScope.WantsAllTenants(_currentUser, scope);
        var scopedTenantId = allTenants ? Guid.Empty : AdminTenantScope.ResolveRead(_currentUser, tenantId);

        var query = new GetWorkflowsQuery
        {
            TenantId = scopedTenantId,
            Take = allTenants ? AdminTenantScope.ClampLimit(limit) : null,
            Status = status,
            ExternalSourceSystem = sourceSystem,
            ExternalReference = externalReference
        };
        
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] Guid? tenantId = null)
    {
        tenantId = AdminTenantScope.ResolveRead(_currentUser, tenantId);

        var query = new GetWorkflowByIdQuery 
        { 
            Id = id,
            TenantId = tenantId.Value
        };
        
        var result = await _mediator.Send(query);
        if (result == null) 
        {
             _logger.LogWarning("GetById Workflow {WorkflowId} not found for Tenant {TenantId}", id, tenantId);
             return NotFound();
        }
        return Ok(result);
    }

    public sealed record SetPriorityRequest(int? Priority);

    /// <summary>CR-12: change or clear (null) the task priority of a running instance (0–100).</summary>
    [HttpPut("{id}/priority")]
    public async Task<IActionResult> SetPriority(Guid id, [FromBody] SetPriorityRequest request)
    {
        if (_currentUser.TenantId == Guid.Empty)
            return Unauthorized("TenantId is missing.");

        try
        {
            await _mediator.Send(new SetWorkflowPriorityCommand(_currentUser.TenantId, id, request.Priority));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        return Ok(new { workflowInstanceId = id, priority = request.Priority });
    }

    [HttpGet("{id}/audit")]
    [HttpGet("{id}/history")]
    public async Task<IActionResult> GetAuditHistory(Guid id, [FromQuery] Guid? tenantId = null)
    {
        if (_currentUser.TenantId == Guid.Empty) return Unauthorized();
        var scopedTenantId = AdminTenantScope.ResolveRead(_currentUser, tenantId);

        var query = new FlowOS.Application.Queries.Admin.GetAdminWorkflowDetailQuery(id, scopedTenantId);
        var result = await _mediator.Send(query);
        if (result == null)
        {
            _logger.LogWarning("GetAuditHistory Workflow {WorkflowId} not found for Tenant {TenantId}", id, scopedTenantId);
            return NotFound();
        }
        return Ok(result);
    }

    [HttpGet("{id}/time-travel")]
    public async Task<IActionResult> GetTimeTravelReplay(Guid id, [FromQuery] Guid? tenantId = null)
    {
        if (_currentUser.TenantId == Guid.Empty) return Unauthorized();
        var scopedTenantId = AdminTenantScope.ResolveRead(_currentUser, tenantId);

        var query = new GetWorkflowTimeTravelReplayQuery(scopedTenantId, id);
        var replay = await _mediator.Send(query);
        if (replay == null)
        {
            _logger.LogWarning("TimeTravel replay for Workflow {WorkflowId} not found for Tenant {TenantId}", id, scopedTenantId);
            return NotFound();
        }
        return Ok(replay);
    }

    [HttpPost("{id}/time-travel/fork")]
    public async Task<IActionResult> SimulateFork(Guid id, [FromBody] FlowOS.Core.Common.Interfaces.WorkflowForkSimulationRequest? request, [FromQuery] Guid? tenantId = null)
    {
        if (_currentUser.TenantId == Guid.Empty) return Unauthorized();
        // A what-if fork only replays history; it never changes the instance, so an admin may run it on any tenant.
        var scopedTenantId = AdminTenantScope.ResolveRead(_currentUser, tenantId);
        if (request == null || string.IsNullOrWhiteSpace(request.AlternativeEvent))
        {
            return BadRequest(new { error = "alternativeEvent is required." });
        }

        var query = new SimulateWorkflowForkQuery(
            scopedTenantId,
            id,
            request.TargetStepIndex,
            request.AlternativeEvent,
            request.AlternativePayload,
            request.SimulatedRoles);

        var result = await _mediator.Send(query);
        return Ok(result);
    }

    [HttpGet("{id}/compensation-path")]
    public async Task<IActionResult> GetCompensationPath(Guid id, [FromQuery] string? failedStepId = null, [FromQuery] Guid? tenantId = null)
    {
        if (_currentUser.TenantId == Guid.Empty) return Unauthorized();
        var scopedTenantId = AdminTenantScope.ResolveRead(_currentUser, tenantId);

        var query = new GetWorkflowCompensationPathQuery(scopedTenantId, id, failedStepId);
        var result = await _mediator.Send(query);
        if (result == null)
        {
            _logger.LogWarning("Compensation path for Workflow {WorkflowId} not found for Tenant {TenantId}", id, scopedTenantId);
            return NotFound();
        }

        return Ok(result);
    }
}

