using System;
using System.Collections.Generic;
using System.Linq;
using FlowOS.Application.Common.Interfaces;
using FlowOS.StateMachines.Engine;
using FlowOS.Workflows.Domain;

namespace FlowOS.Application.Services;

/// <inheritdoc cref="IBusinessRoleResolver"/>
public class BusinessRoleResolver : IBusinessRoleResolver
{
    public IReadOnlyList<string> ResolveCallerRoles(
        WorkflowDefinition definition,
        WorkflowInstance instance,
        Dictionary<string, object>? businessPayload,
        string? callerRef)
    {
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        if (instance == null) throw new ArgumentNullException(nameof(instance));

        if (string.IsNullOrWhiteSpace(callerRef) || definition.BusinessRoles.Count == 0)
            return Array.Empty<string>();

        var held = new List<string>();
        foreach (var role in definition.BusinessRoles)
        {
            if (HoldsRole(role, instance, businessPayload, callerRef))
            {
                held.Add(role.Name);
            }
        }

        return held;
    }

    private static bool HoldsRole(
        BusinessRoleDefinition role,
        WorkflowInstance instance,
        Dictionary<string, object>? businessPayload,
        string callerRef)
    {
        var resolutionType = (role.ResolutionType ?? "Assignment").Trim();

        if (string.Equals(resolutionType, "Static", StringComparison.OrdinalIgnoreCase))
        {
            return role.StaticMembers.Any(member =>
                string.Equals(member, callerRef, StringComparison.OrdinalIgnoreCase));
        }

        if (string.Equals(resolutionType, "Expression", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(role.MemberExpression)) return false;

            var resolved = ExpressionEvaluator.InterpolateTemplate(
                role.MemberExpression,
                businessPayload ?? new Dictionary<string, object>());
            return !string.IsNullOrWhiteSpace(resolved) &&
                   string.Equals(resolved, callerRef, StringComparison.OrdinalIgnoreCase);
        }

        // "Assignment" (default): nothing exists until the running instance itself records who
        // holds the role — see WorkflowInstance.AssignRole.
        return instance.RoleAssignments.TryGetValue(role.Name, out var assignee) &&
               string.Equals(assignee, callerRef, StringComparison.OrdinalIgnoreCase);
    }
}
