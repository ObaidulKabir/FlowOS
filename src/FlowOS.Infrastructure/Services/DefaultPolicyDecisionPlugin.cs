using System;
using System.Collections.Generic;
using FlowOS.Core.Common.Interfaces;
using FlowOS.StateMachines.Engine;

namespace FlowOS.Infrastructure.Services;

/// <summary>
/// Optional named plugin that mirrors existing expression-based decision behavior.
/// This keeps backwards compatibility while allowing explicit provider selection.
/// </summary>
public sealed class DefaultPolicyDecisionPlugin : IPolicyDecisionPlugin
{
    public string ProviderName => "default";

    public PolicyDecisionPluginResult Evaluate(PolicyDecisionPluginContext context)
    {
        foreach (var condition in context.Conditions)
        {
            var expression = condition.Key;
            var target = condition.Value;

            try
            {
                if (context.Payload != null && ExpressionEvaluator.Evaluate(expression, context.Payload))
                {
                    return new PolicyDecisionPluginResult(true, target);
                }
            }
            catch
            {
                // Ignore malformed expressions; continue evaluating next conditions.
            }
        }

        if (context.Conditions.TryGetValue("Default", out var defaultTarget))
        {
            return new PolicyDecisionPluginResult(true, defaultTarget);
        }

        return new PolicyDecisionPluginResult(false, null, "No decision condition matched.");
    }
}
