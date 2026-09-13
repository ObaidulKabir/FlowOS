using System;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowOS.Api.Controllers;

[ApiController]
[Route("api/workflows/{workflowInstanceId:guid}/actions")]
[Authorize]
public class WorkflowActionsController : ControllerBase
{
    private readonly IWorkflowActionHistoryService _actionHistoryService;
    private readonly ICurrentUser _currentUser;

    public WorkflowActionsController(
        IWorkflowActionHistoryService actionHistoryService,
        ICurrentUser currentUser)
    {
        _actionHistoryService = actionHistoryService;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> GetActionHistory(
        [FromRoute] Guid workflowInstanceId,
        [FromQuery] string? stepId = null,
        [FromQuery] string? actionType = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var tenantId = _currentUser.TenantId;
        var history = await _actionHistoryService.GetActionHistoryAsync(
            tenantId,
            workflowInstanceId,
            stepId,
            actionType,
            status,
            page,
            pageSize,
            ct);

        return Ok(history);
    }

    [HttpGet("{logId:guid}")]
    public async Task<IActionResult> GetActionLogById(
        [FromRoute] Guid workflowInstanceId,
        [FromRoute] Guid logId,
        CancellationToken ct = default)
    {
        var tenantId = _currentUser.TenantId;
        var log = await _actionHistoryService.GetActionLogByIdAsync(logId, tenantId, ct);
        if (log == null || log.WorkflowInstanceId != workflowInstanceId)
        {
            return NotFound(new { message = $"Action execution log '{logId}' not found for workflow '{workflowInstanceId}'." });
        }

        return Ok(log);
    }
}
