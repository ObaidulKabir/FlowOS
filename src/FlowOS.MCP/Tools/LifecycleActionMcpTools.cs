using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Application.Commands.Governance;
using FlowOS.Application.Queries.Governance;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Services;
using FlowOS.Domain.Validation;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using MediatR;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Tools;

public class LifecycleActionMcpTools
{
    private readonly IMediator _mediator;
    private readonly WorkflowClassValidator _validator;

    public LifecycleActionMcpTools(IMediator mediator, WorkflowClassValidator validator)
    {
        _mediator = mediator;
        _validator = validator;
    }

    public async Task<CallToolResult> AttachStepAction(JObject args)
    {
        try
        {
            var stepId = args["stepId"]?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(stepId))
                return McpToolResults.Fail("MCP-ARG-001", "stepId is required.");

            var hook = args["hook"]?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(hook) ||
                (!string.Equals(hook, "OnEntry", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(hook, "OnExit", StringComparison.OrdinalIgnoreCase)))
            {
                return McpToolResults.Fail("MCP-ARG-001", "hook must be 'OnEntry' or 'OnExit'.");
            }

            var actionToken = args["action"] as JObject;
            if (actionToken == null)
                return McpToolResults.Fail("MCP-ARG-001", "action object is required.");

            var action = actionToken.ToObject<StepActionBlueprint>();
            if (action == null || string.IsNullOrWhiteSpace(action.ActionType))
                return McpToolResults.Fail("MCP-ARG-001", "action must specify a valid ActionType (Webhook, Notification, PublishEvent).");

            var (blueprint, workflowName, workflowVersion, tenantId, draftId, resolveErr) = await ResolveBlueprintAsync(args);
            if (resolveErr != null) return resolveErr;

            var step = blueprint!.Workflow?.Steps?.FirstOrDefault(s =>
                string.Equals(s.StepId, stepId, StringComparison.OrdinalIgnoreCase));

            if (step == null)
                return McpToolResults.Fail("MCP-NOTFOUND-002", $"Step '{stepId}' not found in workflow blueprint.");

            bool isOnEntry = string.Equals(hook, "OnEntry", StringComparison.OrdinalIgnoreCase);
            var targetList = isOnEntry ? step.OnEntry : step.OnExit;

            int? actionIndex = args["actionIndex"]?.Value<int>();
            if (actionIndex.HasValue && actionIndex.Value >= 0 && actionIndex.Value < targetList.Count)
            {
                targetList[actionIndex.Value] = action;
            }
            else
            {
                targetList.Add(action);
            }

            // Authoritative validation
            var tempEntity = new WorkflowClass(
                tenantId ?? Guid.Empty,
                workflowName ?? "ValidationCheck",
                workflowVersion ?? "1.0.0",
                blueprint);

            var validation = _validator.Validate(tempEntity);
            if (!validation.IsValid)
            {
                return McpToolResults.ValidationFailed(validation);
            }

            // Persist draft if id was provided
            if (draftId.HasValue && tenantId.HasValue)
            {
                await _mediator.Send(new UpdateWorkflowClassCommand(
                    tenantId.Value,
                    draftId.Value,
                    workflowName ?? "UpdatedWorkflow",
                    workflowVersion ?? "1.0.0",
                    blueprint));
            }

            return McpToolResults.Success(new
            {
                stepId = step.StepId,
                hook = isOnEntry ? "OnEntry" : "OnExit",
                totalActions = targetList.Count,
                actionAttached = action,
                blueprint,
                persisted = draftId.HasValue,
                message = $"Lifecycle action '{action.ActionType}' attached to {step.StepId} ({hook}) successfully."
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to attach step action: {ex.Message}");
        }
    }

    public async Task<CallToolResult> RemoveStepAction(JObject args)
    {
        try
        {
            var stepId = args["stepId"]?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(stepId))
                return McpToolResults.Fail("MCP-ARG-001", "stepId is required.");

            var hook = args["hook"]?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(hook) ||
                (!string.Equals(hook, "OnEntry", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(hook, "OnExit", StringComparison.OrdinalIgnoreCase)))
            {
                return McpToolResults.Fail("MCP-ARG-001", "hook must be 'OnEntry' or 'OnExit'.");
            }

            var (blueprint, workflowName, workflowVersion, tenantId, draftId, resolveErr) = await ResolveBlueprintAsync(args);
            if (resolveErr != null) return resolveErr;

            var step = blueprint!.Workflow?.Steps?.FirstOrDefault(s =>
                string.Equals(s.StepId, stepId, StringComparison.OrdinalIgnoreCase));

            if (step == null)
                return McpToolResults.Fail("MCP-NOTFOUND-002", $"Step '{stepId}' not found in workflow blueprint.");

            bool isOnEntry = string.Equals(hook, "OnEntry", StringComparison.OrdinalIgnoreCase);
            var targetList = isOnEntry ? step.OnEntry : step.OnExit;

            int? actionIndex = args["actionIndex"]?.Value<int>();
            var actionType = args["actionType"]?.ToString()?.Trim();
            var target = args["target"]?.ToString()?.Trim();

            bool removed = false;
            if (actionIndex.HasValue && actionIndex.Value >= 0 && actionIndex.Value < targetList.Count)
            {
                targetList.RemoveAt(actionIndex.Value);
                removed = true;
            }
            else if (!string.IsNullOrEmpty(actionType) || !string.IsNullOrEmpty(target))
            {
                var match = targetList.FirstOrDefault(a =>
                    (string.IsNullOrEmpty(actionType) || string.Equals(a.ActionType, actionType, StringComparison.OrdinalIgnoreCase)) &&
                    (string.IsNullOrEmpty(target) || string.Equals(a.Target, target, StringComparison.OrdinalIgnoreCase)));

                if (match != null)
                {
                    targetList.Remove(match);
                    removed = true;
                }
            }

            if (!removed)
                return McpToolResults.Fail("MCP-NOTFOUND-003", "No matching action found to remove.");

            if (draftId.HasValue && tenantId.HasValue)
            {
                await _mediator.Send(new UpdateWorkflowClassCommand(
                    tenantId.Value,
                    draftId.Value,
                    workflowName ?? "UpdatedWorkflow",
                    workflowVersion ?? "1.0.0",
                    blueprint));
            }

            return McpToolResults.Success(new
            {
                stepId = step.StepId,
                hook = isOnEntry ? "OnEntry" : "OnExit",
                remainingActions = targetList.Count,
                blueprint,
                persisted = draftId.HasValue,
                message = $"Lifecycle action removed from {step.StepId} ({hook}) successfully."
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to remove step action: {ex.Message}");
        }
    }

    public async Task<CallToolResult> ListStepActions(JObject args)
    {
        try
        {
            var (blueprint, workflowName, workflowVersion, tenantId, draftId, resolveErr) = await ResolveBlueprintAsync(args);
            if (resolveErr != null) return resolveErr;

            var stepId = args["stepId"]?.ToString()?.Trim();
            var hook = args["hook"]?.ToString()?.Trim();

            var steps = blueprint!.Workflow?.Steps ?? new List<StepBlueprint>();
            if (!string.IsNullOrEmpty(stepId))
            {
                steps = steps.Where(s => string.Equals(s.StepId, stepId, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            var results = new List<object>();
            foreach (var s in steps)
            {
                var stepActions = new
                {
                    stepId = s.StepId,
                    stepType = s.StepType,
                    onEntry = (string.IsNullOrEmpty(hook) || string.Equals(hook, "OnEntry", StringComparison.OrdinalIgnoreCase)) ? s.OnEntry : null,
                    onExit = (string.IsNullOrEmpty(hook) || string.Equals(hook, "OnExit", StringComparison.OrdinalIgnoreCase)) ? s.OnExit : null,
                    totalHooks = (s.OnEntry?.Count ?? 0) + (s.OnExit?.Count ?? 0)
                };
                results.Add(stepActions);
            }

            return McpToolResults.Success(new
            {
                workflow = workflowName ?? "WorkflowBlueprint",
                stepCount = results.Count,
                steps = results
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to list step actions: {ex.Message}");
        }
    }

    private async Task<(WorkflowClassBlueprint? blueprint, string? name, string? version, Guid? tenantId, Guid? draftId, CallToolResult? error)>
        ResolveBlueprintAsync(JObject args)
    {
        var inline = args["blueprint"] as JObject;
        if (inline != null)
        {
            var bp = inline.ToObject<WorkflowClassBlueprint>();
            if (bp == null)
                return (null, null, null, null, null, McpToolResults.Fail("MCP-ARG-001", "Invalid blueprint object."));
            return (bp, "InlineBlueprint", "1.0.0", null, null, null);
        }

        var idStr = args["id"]?.ToString()?.Trim();
        if (string.IsNullOrEmpty(idStr) || !Guid.TryParse(idStr, out var id))
        {
            return (null, null, null, null, null, McpToolResults.Fail("MCP-ARG-001", "Either 'id' or 'blueprint' must be provided."));
        }

        Guid tenantId;
        try
        {
            tenantId = McpTenantResolver.ResolveRequired(args);
        }
        catch (McpToolException ex)
        {
            return (null, null, null, null, null, McpToolResults.Fail(ex.Code, ex.Message));
        }

        var wc = await _mediator.Send(new GetWorkflowClassByIdQuery(tenantId, id));
        if (wc == null || wc.Definition == null)
        {
            return (null, null, null, null, null, McpToolResults.Fail("MCP-NOTFOUND-001", "WorkflowClass not found."));
        }

        return (wc.Definition, wc.Name, wc.Version, tenantId, id, null);
    }
}
