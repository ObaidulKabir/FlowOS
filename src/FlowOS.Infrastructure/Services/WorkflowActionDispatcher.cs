using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Core.Common.Models;
using FlowOS.Infrastructure.Persistence;
using FlowOS.StateMachines.Engine;
using FlowOS.Workflows.Domain;

namespace FlowOS.Infrastructure.Services;

public class WorkflowActionDispatcher : IWorkflowActionDispatcher
{
    private readonly FlowOSDbContext _dbContext;

    public WorkflowActionDispatcher(FlowOSDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task QueueActionsAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string stepId,
        string triggerPhase,
        IReadOnlyList<StepActionDefinition> actions,
        Dictionary<string, object>? payload,
        CancellationToken ct = default)
    {
        if (actions == null || actions.Count == 0) return Task.CompletedTask;

        var contextPayload = payload ?? new Dictionary<string, object>();

        foreach (var action in actions)
        {
            // Check guard condition if defined
            if (!string.IsNullOrWhiteSpace(action.Condition))
            {
                bool allowed = false;
                try
                {
                    allowed = ExpressionEvaluator.Evaluate(action.Condition, contextPayload);
                }
                catch
                {
                    allowed = false;
                }

                if (!allowed) continue;
            }

            // Resolve action payload
            var actionData = new Dictionary<string, object>
            {
                ["tenantId"] = tenantId,
                ["workflowInstanceId"] = workflowInstanceId,
                ["stepId"] = stepId,
                ["triggerPhase"] = triggerPhase,
                ["actionType"] = action.ActionType,
                ["target"] = action.Target ?? string.Empty,
                ["url"] = action.Url ?? string.Empty,
                ["method"] = action.Method ?? "POST",
                ["template"] = action.Template ?? string.Empty
            };

            // Apply payload mappings or include context payload
            if (action.PayloadMapping != null && action.PayloadMapping.Count > 0)
            {
                var mapped = new Dictionary<string, object>();
                foreach (var kvp in action.PayloadMapping)
                {
                    if (contextPayload.TryGetValue(kvp.Value, out var val))
                    {
                        mapped[kvp.Key] = val;
                    }
                    else
                    {
                        mapped[kvp.Key] = kvp.Value;
                    }
                }
                actionData["payload"] = mapped;
            }
            else
            {
                actionData["payload"] = contextPayload;
            }

            var jsonPayload = JsonSerializer.Serialize(actionData);
            var messageType = $"WorkflowAction:{action.ActionType}";

            var outboxMessage = new OutboxMessage(tenantId, messageType, jsonPayload);
            _dbContext.OutboxMessages.Add(outboxMessage);
        }

        return Task.CompletedTask;
    }
}
