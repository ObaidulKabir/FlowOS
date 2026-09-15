using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FlowOS.Application.Commands;
using FlowOS.Application.Common.Exceptions;
using FlowOS.Application.DTOs;
using FlowOS.Application.Queries;
using FlowOS.Domain.ValueObjects;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using MediatR;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Tools;

public class ExecutionTools
{
    private readonly IMediator _mediator;
    private readonly FlowOS.Core.Common.Interfaces.IWorkflowActionHistoryService? _actionHistoryService;
    private readonly FlowOS.Core.Common.Interfaces.IWorkflowTimeTravelService? _timeTravelService;
    private readonly FlowOS.Core.Common.Interfaces.IIdempotencyService? _idempotencyService;
    private readonly FlowOS.Core.Common.Interfaces.IRetryPolicyService? _retryPolicyService;

    public ExecutionTools(
        IMediator mediator,
        FlowOS.Core.Common.Interfaces.IWorkflowActionHistoryService? actionHistoryService = null,
        FlowOS.Core.Common.Interfaces.IWorkflowTimeTravelService? timeTravelService = null,
        FlowOS.Core.Common.Interfaces.IIdempotencyService? idempotencyService = null,
        FlowOS.Core.Common.Interfaces.IRetryPolicyService? retryPolicyService = null)
    {
        _mediator = mediator;
        _actionHistoryService = actionHistoryService;
        _timeTravelService = timeTravelService;
        _idempotencyService = idempotencyService;
        _retryPolicyService = retryPolicyService;
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
            Guid? contextBindingId = null;
            if (args["contextBindingId"] != null)
            {
                if (!Guid.TryParse(args["contextBindingId"]?.ToString(), out var bindingId))
                    return McpToolResults.Fail("MCP-ARG-002", "contextBindingId must be a valid UUID.");
                contextBindingId = bindingId;
            }
            var contextType = args["contextType"]?.ToString()?.Trim();
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

            var selectorCount =
                (workflowDefId.HasValue ? 1 : 0) +
                (workflowClassId != Guid.Empty ? 1 : 0) +
                (!string.IsNullOrWhiteSpace(workflowName) ? 1 : 0) +
                (contextBindingId.HasValue ? 1 : 0) +
                (!string.IsNullOrWhiteSpace(contextType) ? 1 : 0);
            if (selectorCount != 1)
            {
                return McpToolResults.Fail(
                    "MCP-ARG-001",
                    "Specify exactly one of workflowClassId, workflowDefinitionId, workflowName, contextBindingId, or contextType.");
            }
            if ((contextBindingId.HasValue || !string.IsNullOrWhiteSpace(contextType)) && version.HasValue)
            {
                return McpToolResults.Fail(
                    "MCP-ARG-001",
                    "Version cannot be combined with contextBindingId or contextType; the active binding revision selects the runtime version.");
            }

            object? payload = null;
            if (args["payload"] != null)
            {
                payload = JsonSerializer.Deserialize<JsonElement>(
                    args["payload"]!.ToString(Newtonsoft.Json.Formatting.None));
            }

            var command = new StartWorkflowCommand(
                TenantId: tenantId,
                WorkflowDefinitionId: workflowDefId,
                WorkflowName: workflowName,
                Version: version,
                WorkflowClassId: workflowClassId,
                InitialStepId: initialStepId,
                CorrelationId: correlationId ?? Guid.NewGuid(),
                IdempotencyKey: args["idempotencyKey"]?.ToString(),
                Payload: payload,
                ContextBindingId: contextBindingId,
                ContextType: contextType,
                BusinessReference: args["businessReference"]?.ToObject<WorkflowBusinessReference>()
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
        catch (WorkflowContextPayloadException ex)
        {
            return McpToolResults.Fail("MCP-VALIDATION", ex.Message, ex.Errors);
        }
        catch (KeyNotFoundException ex)
        {
            return McpToolResults.Fail("MCP-NOTFOUND-001", ex.Message);
        }
        catch (ArgumentException ex)
        {
            return McpToolResults.Fail("MCP-NOTFOUND-001", ex.Message);
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
                payload = JsonSerializer.Deserialize<JsonElement>(
                    args["payload"]!.ToString(Newtonsoft.Json.Formatting.None));
            }

            var command = new PublishEventCommand(
                TenantId: tenantId,
                WorkflowInstanceId: instanceId,
                EventType: eventType,
                CorrelationId: correlationId,
                Payload: payload,
                IdempotencyKey: args["idempotencyKey"]?.ToString()
            );

            var result = await _mediator.Send(command);

            if (!result)
            {
                return McpToolResults.Fail("MCP-NOTFOUND-001", $"Workflow instance '{instanceId}' not found or event '{eventType}' was not accepted.");
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
        catch (WorkflowContextPayloadException ex)
        {
            return McpToolResults.Fail("MCP-VALIDATION", ex.Message, ex.Errors);
        }
        catch (KeyNotFoundException ex)
        {
            return McpToolResults.Fail("MCP-NOTFOUND-001", ex.Message);
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
                CorrelationId: correlationId,
                IdempotencyKey: args["idempotencyKey"]?.ToString()
            );

            var result = await _mediator.Send(command);

            if (!result)
            {
                return McpToolResults.Fail("MCP-NOTFOUND-001", $"Workflow instance '{instanceId}' or task '{taskId}' not found.");
            }

            return McpToolResults.Success(new
            {
                success = true,
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

            Guid? parentWorkflowInstanceId = null;
            if (args["parentWorkflowInstanceId"] != null && Guid.TryParse(args["parentWorkflowInstanceId"]?.ToString(), out var parsedParentId))
            {
                parentWorkflowInstanceId = parsedParentId;
            }

            var query = new GetWorkflowsQuery
            {
                TenantId = tenantId,
                Status = status,
                ParentWorkflowInstanceId = parentWorkflowInstanceId
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

    public async Task<CallToolResult> GetSubWorkflowTree(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            if (args["workflowInstanceId"] == null || !Guid.TryParse(args["workflowInstanceId"]?.ToString(), out var rootInstanceId))
            {
                return McpToolResults.Fail("MCP-ARG-001", "workflowInstanceId is required and must be a valid UUID.");
            }

            var rootSummary = await _mediator.Send(new FlowOS.Application.Queries.GetWorkflowByIdQuery
            {
                Id = rootInstanceId,
                TenantId = tenantId
            });
            if (rootSummary == null)
            {
                return McpToolResults.Fail("MCP-NOTFOUND-001", $"Workflow instance '{rootInstanceId}' not found.");
            }

            var childSummaries = await _mediator.Send(new GetWorkflowsQuery
            {
                TenantId = tenantId,
                ParentWorkflowInstanceId = rootInstanceId
            });

            return McpToolResults.Success(new
            {
                workflowInstanceId = rootSummary.Id,
                workflowClassName = rootSummary.WorkflowClassName,
                status = rootSummary.Status,
                currentStepId = rootSummary.CurrentStepId,
                currentState = rootSummary.CurrentState,
                createdAt = rootSummary.CreatedAt,
                completedAt = rootSummary.CompletedAt,
                parentWorkflowInstanceId = rootSummary.ParentWorkflowInstanceId,
                parentStepId = rootSummary.ParentStepId,
                hasChildren = childSummaries.Count > 0,
                childCount = childSummaries.Count,
                children = childSummaries.Select(c => new
                {
                    workflowInstanceId = c.Id,
                    workflowClassName = c.WorkflowClassName,
                    parentStepId = c.ParentStepId,
                    status = c.Status,
                    currentStepId = c.CurrentStepId,
                    currentState = c.CurrentState,
                    createdAt = c.CreatedAt,
                    completedAt = c.CompletedAt
                }).ToList()
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to retrieve subworkflow tree: {ex.Message}");
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
                return McpToolResults.Fail("MCP-NOTFOUND-001", $"Workflow instance '{instanceId}' not found.");
            }

            IReadOnlyList<FlowOS.Core.Common.Interfaces.WorkflowActionExecutionLogDto>? actionLogs = null;
            if (_actionHistoryService != null)
            {
                try
                {
                    actionLogs = await _actionHistoryService.GetActionHistoryAsync(tenantId, instanceId, pageSize: 20);
                }
                catch
                {
                    actionLogs = null;
                }
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
                timeline = detail.Timeline,
                actions = actionLogs?.Select(a => (object)new
                {
                    id = a.Id,
                    stepId = a.StepId,
                    triggerPhase = a.TriggerPhase,
                    actionType = a.ActionType,
                    target = a.Target,
                    status = a.Status,
                    executedAtUtc = a.ExecutedAtUtc,
                    durationMs = a.DurationMs,
                    httpStatusCode = a.HttpStatusCode,
                    errorMessage = a.ErrorMessage
                }).ToList() ?? (object)Array.Empty<object>()
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

    public async Task<CallToolResult> ReplayWorkflowHistory(JObject args)
    {
        try
        {
            if (_timeTravelService == null)
            {
                return McpToolResults.Fail("MCP-INTERNAL", "Time-travel replay service is not registered.");
            }

            var tenantId = McpTenantResolver.ResolveRequired(args);
            if (args["workflowInstanceId"] == null || !Guid.TryParse(args["workflowInstanceId"]?.ToString(), out var instanceId))
            {
                return McpToolResults.Fail("MCP-ARG-001", "workflowInstanceId is required and must be a valid UUID.");
            }

            var replay = await _timeTravelService.GetReplayTimelineAsync(tenantId, instanceId);
            if (replay == null)
            {
                return McpToolResults.Fail("MCP-NOTFOUND-001", $"Workflow instance '{instanceId}' was not found.");
            }

            return McpToolResults.Success(new
            {
                workflowInstanceId = replay.WorkflowInstanceId,
                workflowClassName = replay.WorkflowClassName,
                workflowVersion = replay.WorkflowVersion,
                status = replay.Status,
                totalSteps = replay.TotalSteps,
                contextBindingId = replay.ContextBindingId,
                contextBindingRevisionId = replay.ContextBindingRevisionId,
                contextType = replay.ContextType,
                sourceSystem = replay.SourceSystem,
                externalEntityId = replay.ExternalEntityId,
                snapshots = replay.Snapshots.Select(s => new
                {
                    stepIndex = s.StepIndex,
                    timestamp = s.Timestamp,
                    eventId = s.EventId,
                    eventType = s.EventType,
                    fromStepId = s.FromStepId,
                    toStepId = s.ToStepId,
                    activeStepIds = s.ActiveStepIds,
                    fromState = s.FromState,
                    toState = s.ToState,
                    actorId = s.ActorId,
                    variables = s.Variables,
                    contextualEventType = s.ContextualEventType,
                    canonicalEventType = s.CanonicalEventType,
                    contextBindingRevisionId = s.ContextBindingRevisionId,
                    actionLogs = s.ActionLogs.Select(a => new
                    {
                        id = a.Id,
                        stepId = a.StepId,
                        triggerPhase = a.TriggerPhase,
                        actionType = a.ActionType,
                        target = a.Target,
                        status = a.Status,
                        executedAtUtc = a.ExecutedAtUtc,
                        durationMs = a.DurationMs
                    }),
                    summary = s.Summary
                })
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to replay workflow history: {ex.Message}");
        }
    }

    public async Task<CallToolResult> ForkWorkflowSimulation(JObject args)
    {
        try
        {
            if (_timeTravelService == null)
            {
                return McpToolResults.Fail("MCP-INTERNAL", "Time-travel replay service is not registered.");
            }

            var tenantId = McpTenantResolver.ResolveRequired(args);
            if (args["workflowInstanceId"] == null || !Guid.TryParse(args["workflowInstanceId"]?.ToString(), out var instanceId))
            {
                return McpToolResults.Fail("MCP-ARG-001", "workflowInstanceId is required and must be a valid UUID.");
            }

            var alternativeEvent = args["alternativeEvent"]?.ToString() ?? args["eventType"]?.ToString();
            if (string.IsNullOrWhiteSpace(alternativeEvent))
            {
                return McpToolResults.Fail("MCP-ARG-001", "alternativeEvent is required.");
            }

            var targetStepIndex = args["targetStepIndex"]?.Value<int>() ?? 0;
            object? payload = args["alternativePayload"] ?? args["payload"];
            var simulatedRoles = args["simulatedRoles"] is JArray roles
                ? roles.Values<string>().Where(role => !string.IsNullOrWhiteSpace(role)).Select(role => role!).ToList()
                : new List<string>();

            var result = await _timeTravelService.SimulateForkAsync(
                tenantId,
                instanceId,
                targetStepIndex,
                alternativeEvent,
                payload,
                simulatedRoles);

            return McpToolResults.Success(new
            {
                forkFromStepIndex = result.ForkFromStepIndex,
                baseStepId = result.BaseStepId,
                baseState = result.BaseState,
                alternativeEvent = result.AlternativeEvent,
                projectedStepId = result.ProjectedStepId,
                projectedState = result.ProjectedState,
                isAllowed = result.IsAllowed,
                reason = result.Reason,
                projectedActions = result.ProjectedActions,
                simulatedRoles = result.SimulatedRoles,
                contextualEventType = result.ContextualEventType,
                canonicalEventType = result.CanonicalEventType,
                contextBindingId = result.ContextBindingId,
                contextBindingRevisionId = result.ContextBindingRevisionId,
                contextType = result.ContextType,
                projectedCanonicalContext = result.ProjectedCanonicalContext,
                sideEffects = "none"
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to simulate workflow fork: {ex.Message}");
        }
    }

    public async Task<CallToolResult> PlanWorkflowCompensationPath(JObject args)
    {
        try
        {
            if (_timeTravelService == null)
            {
                return McpToolResults.Fail("MCP-INTERNAL", "Time-travel replay service is not registered.");
            }

            var tenantId = McpTenantResolver.ResolveRequired(args);
            if (args["workflowInstanceId"] == null || !Guid.TryParse(args["workflowInstanceId"]?.ToString(), out var instanceId))
            {
                return McpToolResults.Fail("MCP-ARG-001", "workflowInstanceId is required and must be a valid UUID.");
            }

            var failedStepId = args["failedStepId"]?.ToString();
            var result = await _timeTravelService.PlanCompensationPathAsync(tenantId, instanceId, failedStepId);
            if (result == null)
            {
                return McpToolResults.Fail("MCP-NOTFOUND-001", $"Workflow instance '{instanceId}' was not found.");
            }

            return McpToolResults.Success(new
            {
                workflowInstanceId = result.WorkflowInstanceId,
                failedStepId = result.FailedStepId,
                executedStepIds = result.ExecutedStepIds,
                isFullyCompensable = result.IsFullyCompensable,
                orderedCompensations = result.OrderedCompensations.Select(p => new
                {
                    stepId = p.StepId,
                    actionCount = p.ActionCount,
                    actions = p.Actions.Select(a => new
                    {
                        stepId = a.StepId,
                        hook = a.Hook,
                        actionType = a.ActionType,
                        target = a.Target,
                        capability = a.Capability,
                        url = a.Url,
                        condition = a.Condition
                    })
                }),
                blockedSteps = result.BlockedSteps
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to plan workflow compensation path: {ex.Message}");
        }
    }

    public async Task<CallToolResult> RegisterIdempotencyKey(JObject args)
    {
        try
        {
            if (_idempotencyService == null)
            {
                return McpToolResults.Fail("MCP-INTERNAL", "Idempotency service is not registered.");
            }

            var tenantId = McpTenantResolver.ResolveRequired(args);
            var operation = args["operationName"]?.ToString();
            var key = args["idempotencyKey"]?.ToString();
            if (string.IsNullOrWhiteSpace(operation) || string.IsNullOrWhiteSpace(key))
            {
                return McpToolResults.Fail("MCP-ARG-001", "operationName and idempotencyKey are required.");
            }

            var started = await _idempotencyService.TryBeginAsync(tenantId, operation, key);
            return McpToolResults.Success(new
            {
                operationName = operation,
                idempotencyKey = key,
                registered = started,
                status = started ? "Pending" : "Exists"
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to register idempotency key: {ex.Message}");
        }
    }

    public async Task<CallToolResult> InspectIdempotencyStatus(JObject args)
    {
        try
        {
            if (_idempotencyService == null)
            {
                return McpToolResults.Fail("MCP-INTERNAL", "Idempotency service is not registered.");
            }

            var tenantId = McpTenantResolver.ResolveRequired(args);
            var operation = args["operationName"]?.ToString();
            var key = args["idempotencyKey"]?.ToString();
            if (string.IsNullOrWhiteSpace(operation) || string.IsNullOrWhiteSpace(key))
            {
                return McpToolResults.Fail("MCP-ARG-001", "operationName and idempotencyKey are required.");
            }

            var status = await _idempotencyService.GetStatusAsync(tenantId, operation, key);
            if (status == null)
            {
                return McpToolResults.Fail("MCP-NOTFOUND-001", "Idempotency key not found.");
            }

            return McpToolResults.Success(new
            {
                status.TenantId,
                status.OperationName,
                status.IdempotencyKey,
                status.Status,
                status.CreatedAtUtc,
                status.UpdatedAtUtc
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to inspect idempotency status: {ex.Message}");
        }
    }

    public Task<CallToolResult> PreviewRetryPolicy(JObject args)
    {
        try
        {
            if (_retryPolicyService == null)
            {
                return Task.FromResult(McpToolResults.Fail("MCP-INTERNAL", "Retry policy service is not registered."));
            }

            var currentRetryCount = args["currentRetryCount"]?.Value<int>() ?? 0;
            var maxRetries = args["maxRetries"]?.Value<int>() ?? 5;
            var baseDelaySeconds = args["baseDelaySeconds"]?.Value<int>() ?? 2;
            var strategy = args["strategy"]?.ToString() ?? "exponential";
            var maxDelaySeconds = args["maxDelaySeconds"]?.Value<int>() ?? 3600;
            var errorMessage = args["errorMessage"]?.ToString();

            var preview = _retryPolicyService.Preview(
                currentRetryCount: currentRetryCount,
                maxRetries: maxRetries,
                baseDelaySeconds: baseDelaySeconds,
                strategy: strategy,
                maxDelaySeconds: maxDelaySeconds,
                errorMessage: errorMessage);

            return Task.FromResult(McpToolResults.Success(new
            {
                preview.Strategy,
                preview.MaxRetries,
                preview.CurrentRetryCount,
                preview.BaseDelaySeconds,
                preview.MaxDelaySeconds,
                preview.ShouldRetryNow,
                preview.Classification,
                attempts = preview.PlannedAttempts.Select(a => new
                {
                    a.AttemptNumber,
                    a.DelaySeconds
                }),
                preview.PreviewGeneratedAtUtc
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResults.Fail("MCP-INTERNAL", $"Failed to preview retry policy: {ex.Message}"));
        }
    }
}
