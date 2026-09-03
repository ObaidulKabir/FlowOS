using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Application.Commands;
using FlowOS.Application.DTOs;
using FlowOS.Application.Queries;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using MediatR;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Tools;

public class ExecutionTools
{
    private readonly IMediator _mediator;

    public ExecutionTools(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<CallToolResult> StartWorkflow(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);

            Guid? workflowDefId = null;
            if (args["workflowDefinitionId"] != null && Guid.TryParse(args["workflowDefinitionId"]?.ToString(), out var defId))
            {
                workflowDefId = defId;
            }

            Guid workflowClassId = Guid.Empty;
            if (args["workflowClassId"] != null && Guid.TryParse(args["workflowClassId"]?.ToString(), out var clsId))
            {
                workflowClassId = clsId;
            }

            var workflowName = args["workflowName"]?.ToString();
            var initialStepId = args["initialStepId"]?.ToString();

            Guid? correlationId = null;
            if (args["correlationId"] != null && Guid.TryParse(args["correlationId"]?.ToString(), out var corrId))
            {
                correlationId = corrId;
            }

            int? version = null;
            if (args["version"] != null && int.TryParse(args["version"]?.ToString(), out var ver))
            {
                version = ver;
            }

            if (workflowDefId == null && workflowClassId == Guid.Empty && string.IsNullOrWhiteSpace(workflowName))
            {
                return McpToolResults.Fail("MCP-ARG-001", "Either workflowClassId, workflowDefinitionId, or workflowName is required.");
            }

            var command = new StartWorkflowCommand(
                TenantId: tenantId,
                WorkflowDefinitionId: workflowDefId,
                WorkflowName: workflowName,
                Version: version,
                WorkflowClassId: workflowClassId,
                InitialStepId: initialStepId,
                CorrelationId: correlationId ?? Guid.NewGuid()
            );

            var instanceId = await _mediator.Send(command);

            return McpToolResults.Success(new
            {
                workflowInstanceId = instanceId,
                tenantId,
                status = "Running",
                correlationId = command.CorrelationId,
                message = "Workflow instance started successfully."
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to start workflow instance: {ex.Message}");
        }
    }

    public async Task<CallToolResult> PublishEvent(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);

            var instanceIdStr = args["workflowInstanceId"]?.ToString() ?? args["instanceId"]?.ToString();
            if (string.IsNullOrWhiteSpace(instanceIdStr) || !Guid.TryParse(instanceIdStr, out var instanceId))
            {
                return McpToolResults.Fail("MCP-ARG-002", "workflowInstanceId must be a valid UUID.");
            }

            var eventType = args["eventType"]?.ToString();
            if (string.IsNullOrWhiteSpace(eventType))
            {
                return McpToolResults.Fail("MCP-ARG-001", "eventType is required (e.g. EVT-SUBMIT, EVT-APPROVE).");
            }

            Guid? correlationId = null;
            if (args["correlationId"] != null && Guid.TryParse(args["correlationId"]?.ToString(), out var corrId))
            {
                correlationId = corrId;
            }

            object? payload = null;
            if (args["payload"] != null)
            {
                payload = args["payload"]?.ToObject<object>();
            }

            var command = new PublishEventCommand(
                TenantId: tenantId,
                WorkflowInstanceId: instanceId,
                EventType: eventType,
                CorrelationId: correlationId,
                Payload: payload
            );

            var result = await _mediator.Send(command);

            if (!result)
            {
                return McpToolResults.Fail("MCP-EXEC-001", $"Event '{eventType}' was not accepted by the workflow state machine.");
            }

            return McpToolResults.Success(new
            {
                success = true,
                workflowInstanceId = instanceId,
                eventType,
                message = $"Event '{eventType}' published and processed successfully."
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to publish event: {ex.Message}");
        }
    }

    public async Task<CallToolResult> CompleteTask(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);

            var instanceIdStr = args["workflowInstanceId"]?.ToString() ?? args["instanceId"]?.ToString();
            if (string.IsNullOrWhiteSpace(instanceIdStr) || !Guid.TryParse(instanceIdStr, out var instanceId))
            {
                return McpToolResults.Fail("MCP-ARG-002", "workflowInstanceId must be a valid UUID.");
            }

            var taskIdStr = args["taskId"]?.ToString();
            if (string.IsNullOrWhiteSpace(taskIdStr) || !Guid.TryParse(taskIdStr, out var taskId))
            {
                return McpToolResults.Fail("MCP-ARG-002", "taskId must be a valid UUID.");
            }

            Guid? correlationId = null;
            if (args["correlationId"] != null && Guid.TryParse(args["correlationId"]?.ToString(), out var corrId))
            {
                correlationId = corrId;
            }

            var command = new CompleteTaskCommand(
                TenantId: tenantId,
                WorkflowInstanceId: instanceId,
                TaskId: taskId,
                CorrelationId: correlationId
            );

            var result = await _mediator.Send(command);

            return McpToolResults.Success(new
            {
                success = result,
                workflowInstanceId = instanceId,
                taskId,
                message = "Task completed successfully."
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to complete task: {ex.Message}");
        }
    }

    public async Task<CallToolResult> ListWorkflowInstances(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);

            FlowOS.Workflows.Enums.WorkflowInstanceStatus? status = null;
            if (args["status"] != null && Enum.TryParse<FlowOS.Workflows.Enums.WorkflowInstanceStatus>(args["status"]?.ToString(), true, out var parsedStatus))
            {
                status = parsedStatus;
            }

            var query = new GetWorkflowsQuery
            {
                TenantId = tenantId,
                Status = status
            };

            var instances = await _mediator.Send(query);

            return McpToolResults.Success(new
            {
                instances = instances ?? new List<WorkflowSummaryDto>()
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to list workflow instances: {ex.Message}");
        }
    }

    public async Task<CallToolResult> GetWorkflowHistory(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);

            if (args["workflowInstanceId"] == null || !Guid.TryParse(args["workflowInstanceId"]?.ToString(), out var instanceId))
            {
                return McpToolResults.Fail("MCP-ARG-001", "workflowInstanceId is required and must be a valid UUID.");
            }

            var query = new FlowOS.Application.Queries.Admin.GetAdminWorkflowDetailQuery(instanceId, tenantId);
            var detail = await _mediator.Send(query);

            if (detail == null)
            {
                return McpToolResults.Fail("MCP-NOT-FOUND", $"Workflow instance '{instanceId}' not found for tenant '{tenantId}'.");
            }

            return McpToolResults.Success(new
            {
                workflowInstanceId = detail.Id,
                definitionName = detail.DefinitionName,
                version = detail.Version,
                currentStepId = detail.CurrentStepId,
                status = detail.Status,
                correlationId = detail.CorrelationId,
                createdAt = detail.CreatedAt,
                timeline = detail.Timeline
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to retrieve workflow history: {ex.Message}");
        }
    }
}
