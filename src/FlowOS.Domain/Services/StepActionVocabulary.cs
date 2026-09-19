using System;
using FlowOS.Domain.Blueprints;

namespace FlowOS.Domain.Services;

/// <summary>
/// Maps the current connector vocabulary onto the stored action shape. Blueprints may say
/// <c>InvokeConnector</c>/<c>connector</c>; the runtime keeps storing <c>InvokeCapability</c>/<c>capability</c>
/// so existing definitions and tenants keep working.
/// </summary>
public static class StepActionVocabulary
{
    public const string InvokeConnectorActionType = "InvokeConnector";
    public const string InvokeCapabilityActionType = "InvokeCapability";

    public static bool IsConnectorInvocation(string? actionType)
    {
        var normalized = actionType?.Trim();
        return string.Equals(normalized, InvokeConnectorActionType, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, InvokeCapabilityActionType, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Collapses the connector spelling onto the stored action type.</summary>
    public static string NormalizeActionType(string? actionType)
    {
        var normalized = actionType?.Trim() ?? string.Empty;
        return string.Equals(normalized, InvokeConnectorActionType, StringComparison.OrdinalIgnoreCase)
            ? InvokeCapabilityActionType
            : normalized;
    }

    /// <summary>Connector key for an action: <c>connector</c>, then legacy <c>capability</c>, then legacy <c>target</c>.</summary>
    public static string? ResolveConnectorName(StepActionBlueprint action)
    {
        if (action == null) return null;
        if (!string.IsNullOrWhiteSpace(action.Connector)) return action.Connector.Trim();
        if (!string.IsNullOrWhiteSpace(action.Capability)) return action.Capability.Trim();
        return string.IsNullOrWhiteSpace(action.Target) ? null : action.Target.Trim();
    }
}
