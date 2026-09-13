using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Core.Common.Interfaces;
using FlowOS.MCP.Models;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Tools;

public class PluginDiscoveryMcpTools
{
    private readonly IEnumerable<IWorkflowActionPlugin> _actionPlugins;
    private readonly IEnumerable<IPolicyDecisionPlugin> _decisionPlugins;
    private readonly IConfiguration _configuration;

    public PluginDiscoveryMcpTools(
        IEnumerable<IWorkflowActionPlugin> actionPlugins,
        IEnumerable<IPolicyDecisionPlugin> decisionPlugins,
        IConfiguration configuration)
    {
        _actionPlugins = actionPlugins ?? Enumerable.Empty<IWorkflowActionPlugin>();
        _decisionPlugins = decisionPlugins ?? Enumerable.Empty<IPolicyDecisionPlugin>();
        _configuration = configuration;
    }

    public Task<CallToolResult> ListRegisteredPlugins(JObject args)
    {
        try
        {
            var includeWildcard = args["includeWildcard"]?.Value<bool>() ?? true;

            var actionPlugins = _actionPlugins
                .Select(p => p.ActionType?.Trim())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(p => includeWildcard || !string.Equals(p, "*", StringComparison.Ordinal))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Select(p => new
                {
                    actionType = p,
                    isWildcard = string.Equals(p, "*", StringComparison.Ordinal),
                    kind = IsBuiltInActionType(p) ? "built-in" : "custom"
                })
                .ToList();

            var decisionPlugins = _decisionPlugins
                .Select(p => p.ProviderName?.Trim())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Select(p => new
                {
                    providerName = p,
                    kind = string.Equals(p, "default", StringComparison.OrdinalIgnoreCase) ? "built-in" : "custom"
                })
                .ToList();

            var allowWildcardFallback = !bool.TryParse(
                _configuration["FlowOS:Actions:AllowWildcardPluginFallback"],
                out var wildcardSetting) || wildcardSetting;

            var rejectUnknownActionTypes = bool.TryParse(
                _configuration["FlowOS:Actions:RejectUnknownActionTypes"],
                out var rejectUnknown) && rejectUnknown;

            return Task.FromResult(McpToolResults.Success(new
            {
                totalActionPlugins = actionPlugins.Count,
                totalDecisionPlugins = decisionPlugins.Count,
                actionPlugins,
                decisionPlugins,
                conventions = new
                {
                    customActionAliasPrefixes = new[] { "plugin:", "plugin." },
                    decisionProviderField = "workflow.steps[].decisionProvider"
                },
                runtimePolicy = new
                {
                    allowWildcardPluginFallback = allowWildcardFallback,
                    rejectUnknownActionTypes
                }
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResults.Fail(
                "MCP-INTERNAL",
                $"Failed to list registered plugins: {ex.Message}"));
        }
    }

    private static bool IsBuiltInActionType(string? actionType)
    {
        if (string.IsNullOrWhiteSpace(actionType)) return false;

        return string.Equals(actionType, "Webhook", StringComparison.OrdinalIgnoreCase)
            || string.Equals(actionType, "Notification", StringComparison.OrdinalIgnoreCase)
            || string.Equals(actionType, "PublishEvent", StringComparison.OrdinalIgnoreCase)
            || string.Equals(actionType, "InvokeCapability", StringComparison.OrdinalIgnoreCase)
            || string.Equals(actionType, "*", StringComparison.Ordinal);
    }
}
