using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Common.Models;
using FlowOS.Infrastructure.Persistence;
using FlowOS.StateMachines.Engine;
using FlowOS.Workflows.Domain;
using Microsoft.Extensions.Configuration;

namespace FlowOS.Infrastructure.Services;

public class WorkflowActionDispatcher : IWorkflowActionDispatcher
{
    private readonly FlowOSDbContext _dbContext;
    private readonly IWorkflowActionPluginRegistry? _pluginRegistry;
    private readonly IPluginBindingRegistryService? _pluginBindingRegistry;
    private readonly bool _rejectUnknownActionTypes;

    public WorkflowActionDispatcher(
        FlowOSDbContext dbContext,
        IWorkflowActionPluginRegistry? pluginRegistry = null,
        IConfiguration? configuration = null,
        IPluginBindingRegistryService? pluginBindingRegistry = null)
    {
        _dbContext = dbContext;
        _pluginRegistry = pluginRegistry;
        _pluginBindingRegistry = pluginBindingRegistry;
        _rejectUnknownActionTypes = bool.TryParse(
            configuration?["FlowOS:Actions:RejectUnknownActionTypes"],
            out var rejectUnknown) && rejectUnknown;
    }

    public async Task QueueActionsAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string stepId,
        string triggerPhase,
        IReadOnlyList<StepActionDefinition> actions,
        Dictionary<string, object>? payload,
        CancellationToken ct = default)
    {
        if (actions == null || actions.Count == 0) return;

        var contextPayload = payload ?? new Dictionary<string, object>();
        var actionBindings = await ResolveActionBindingsAsync(tenantId, actions, ct);

        foreach (var action in actions)
        {
            var originalActionType = action.ActionType?.Trim() ?? string.Empty;

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

            var mappedActionType = actionBindings.TryGetValue(originalActionType, out var boundProvider)
                ? boundProvider
                : originalActionType;
            var effectiveAction = CloneWithActionType(action, mappedActionType);

            // Interpolate dynamic template, target, and url
            var resolvedTemplate = ExpressionEvaluator.InterpolateTemplate(effectiveAction.Template, contextPayload);
            var resolvedTarget = ExpressionEvaluator.InterpolateTemplate(effectiveAction.Target, contextPayload);
            var resolvedCapability = ExpressionEvaluator.InterpolateTemplate(effectiveAction.Capability, contextPayload);
            var resolvedUrl = ExpressionEvaluator.InterpolateTemplate(effectiveAction.Url, contextPayload);
            if (string.Equals(effectiveAction.ActionType, "InvokeCapability", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(resolvedCapability))
            {
                resolvedCapability = resolvedTarget;
            }

            // Resolve custom headers with token interpolation
            Dictionary<string, string>? resolvedHeaders = null;
            if (effectiveAction.Headers != null && effectiveAction.Headers.Count > 0)
            {
                resolvedHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (headerKey, headerVal) in effectiveAction.Headers)
                {
                    resolvedHeaders[headerKey] = ExpressionEvaluator.InterpolateTemplate(headerVal, contextPayload);
                }
            }

            // Apply payload mappings with dynamic expression evaluation or include context payload
            object resolvedActionPayload;
            if (effectiveAction.PayloadMapping != null && effectiveAction.PayloadMapping.Count > 0)
            {
                var mapped = new Dictionary<string, object>();
                foreach (var kvp in effectiveAction.PayloadMapping)
                {
                    var val = ExpressionEvaluator.EvaluateValue(kvp.Value, contextPayload);
                    mapped[kvp.Key] = val ?? kvp.Value;
                }
                resolvedActionPayload = mapped;
            }
            else
            {
                resolvedActionPayload = contextPayload;
            }

            string messageType = $"WorkflowAction:{effectiveAction.ActionType}";
            object payloadToSerialize = BuildDefaultPayload(
                tenantId,
                workflowInstanceId,
                stepId,
                triggerPhase,
                effectiveAction,
                resolvedTarget,
                resolvedCapability,
                resolvedUrl,
                resolvedTemplate,
                resolvedHeaders,
                resolvedActionPayload);

            if (_pluginRegistry != null && _pluginRegistry.TryResolve(effectiveAction.ActionType, out var plugin))
            {
                var pluginResult = plugin.BuildMessage(new WorkflowActionPluginContext(
                    TenantId: tenantId,
                    WorkflowInstanceId: workflowInstanceId,
                    StepId: stepId,
                    TriggerPhase: triggerPhase,
                    Action: effectiveAction,
                    RuntimePayload: contextPayload,
                    ResolvedTarget: resolvedTarget,
                    ResolvedCapability: resolvedCapability,
                    ResolvedUrl: resolvedUrl,
                    ResolvedTemplate: resolvedTemplate,
                    ResolvedHeaders: resolvedHeaders,
                    ResolvedActionPayload: resolvedActionPayload));

                messageType = pluginResult.MessageType;
                payloadToSerialize = pluginResult.MessagePayload;
            }
            else if (_rejectUnknownActionTypes && !IsKnownBuiltInActionType(effectiveAction.ActionType))
            {
                throw new InvalidOperationException(
                    $"Unknown workflow action type '{effectiveAction.ActionType}'. Register an IWorkflowActionPlugin or enable wildcard fallback.");
            }

            if (!string.Equals(originalActionType, effectiveAction.ActionType, StringComparison.OrdinalIgnoreCase)
                && payloadToSerialize is IDictionary<string, object> payloadObj)
            {
                payloadObj["sourceActionType"] = originalActionType;
            }

            var jsonPayload = JsonSerializer.Serialize(payloadToSerialize);

            var outboxMessage = new OutboxMessage(tenantId, messageType, jsonPayload);
            _dbContext.OutboxMessages.Add(outboxMessage);
        }
    }

    private static Dictionary<string, object> BuildDefaultPayload(
        Guid tenantId,
        Guid workflowInstanceId,
        string stepId,
        string triggerPhase,
        StepActionDefinition action,
        string? resolvedTarget,
        string? resolvedCapability,
        string? resolvedUrl,
        string? resolvedTemplate,
        Dictionary<string, string>? resolvedHeaders,
        object resolvedActionPayload)
    {
        var actionData = new Dictionary<string, object>
        {
            ["tenantId"] = tenantId,
            ["workflowInstanceId"] = workflowInstanceId,
            ["stepId"] = stepId,
            ["triggerPhase"] = triggerPhase,
            ["actionType"] = action.ActionType,
            ["target"] = resolvedTarget!,
            ["capability"] = resolvedCapability ?? resolvedTarget!,
            ["url"] = resolvedUrl!,
            ["method"] = action.Method ?? "POST",
            ["template"] = resolvedTemplate!,
            ["signPayload"] = action.SignPayload,
            ["secretName"] = action.SecretName ?? string.Empty,
            ["payload"] = resolvedActionPayload
        };

        if (resolvedHeaders != null)
        {
            actionData["headers"] = resolvedHeaders;
        }

        return actionData;
    }

    private static bool IsKnownBuiltInActionType(string? actionType)
    {
        if (string.IsNullOrWhiteSpace(actionType)) return false;

        return actionType.Equals("Webhook", StringComparison.OrdinalIgnoreCase)
            || actionType.Equals("Notification", StringComparison.OrdinalIgnoreCase)
            || actionType.Equals("PublishEvent", StringComparison.OrdinalIgnoreCase)
            || actionType.Equals("InvokeCapability", StringComparison.OrdinalIgnoreCase)
            || actionType.Equals("Slack", StringComparison.OrdinalIgnoreCase)
            || actionType.Equals("Email", StringComparison.OrdinalIgnoreCase)
            || actionType.Equals("WhatsApp", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<string, string>> ResolveActionBindingsAsync(
        Guid tenantId,
        IReadOnlyList<StepActionDefinition> actions,
        CancellationToken ct)
    {
        if (_pluginBindingRegistry == null || actions.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var registered = await _pluginBindingRegistry.ResolveBindingsAsync(tenantId, PluginBindingTypes.Action, ct);
        if (registered.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var inputActionTypes = actions
            .Where(a => !string.IsNullOrWhiteSpace(a.ActionType))
            .Select(a => a.ActionType.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var actionType in inputActionTypes)
        {
            if (registered.TryGetValue(actionType, out var mappedProvider) &&
                !string.IsNullOrWhiteSpace(mappedProvider))
            {
                result[actionType] = mappedProvider;
            }
        }

        return result;
    }

    private static StepActionDefinition CloneWithActionType(StepActionDefinition action, string actionType)
    {
        if (string.Equals(action.ActionType, actionType, StringComparison.OrdinalIgnoreCase))
        {
            return action;
        }

        return new StepActionDefinition(actionType)
        {
            Target = action.Target,
            Capability = action.Capability,
            Url = action.Url,
            Method = action.Method,
            Template = action.Template,
            Condition = action.Condition,
            Headers = action.Headers,
            PayloadMapping = action.PayloadMapping,
            SignPayload = action.SignPayload,
            SecretName = action.SecretName
        };
    }
}
