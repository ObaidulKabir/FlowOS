using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Core.Interfaces;
using FlowOS.StateMachines.Engine;
using FlowOS.Workflows.Domain;

namespace FlowOS.Application.Services;

/// <inheritdoc cref="IBusinessRoleResolver"/>
public class BusinessRoleResolver : IBusinessRoleResolver
{
    private readonly ITeamRepository? _teamRepository;
    private readonly ICurrentUser? _currentUser;

    public BusinessRoleResolver(ITeamRepository? teamRepository = null, ICurrentUser? currentUser = null)
    {
        _teamRepository = teamRepository;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<string>> ResolveCallerRolesAsync(
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
            if (await HoldsRoleAsync(role, instance, businessPayload, callerRef))
            {
                held.Add(role.Name);
            }
        }

        return held;
    }

    private async Task<bool> HoldsRoleAsync(
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
        
        if (string.Equals(resolutionType, "Team", StringComparison.OrdinalIgnoreCase))
        {
            // If the role is resolved via Teams, we need the caller's actual Guid to check DB membership.
            // Since CallerRef is string, we assume it's the TenantUserId Guid as string, 
            // or we use ICurrentUser if available.
            if (!Guid.TryParse(callerRef, out var userId) && _currentUser != null && Guid.TryParse(_currentUser.Id, out var curId))
            {
                userId = curId;
            }
            
            if (userId != Guid.Empty && _teamRepository != null)
            {
                // To authorize as a Team, the user must be in a team that holds ALL of the role's required capabilities.
                return await _teamRepository.IsUserAuthorizedForCapabilitiesAsync(
                    instance.TenantId, 
                    userId, 
                    role.Capabilities);
            }
            return false;
        }

        // "Assignment" (default): nothing exists until the running instance itself records who
        // holds the role — see WorkflowInstance.AssignRole.
        if (instance.RoleAssignments.TryGetValue(role.Name, out var assignee))
        {
            if (string.Equals(assignee, callerRef, StringComparison.OrdinalIgnoreCase))
                return true;

            // Handle system-generated team assignments (team:{teamId}:{hierarchyOrder})
            if (assignee.StartsWith("team:", StringComparison.OrdinalIgnoreCase) && _teamRepository != null)
            {
                var parts = assignee.Split(':');
                if (parts.Length == 3 && 
                    Guid.TryParse(parts[1], out var teamId) && 
                    int.TryParse(parts[2], out var requiredOrder))
                {
                    if (!Guid.TryParse(callerRef, out var userId) && _currentUser != null && Guid.TryParse(_currentUser.Id, out var curId))
                    {
                        userId = curId;
                    }

                    if (userId != Guid.Empty)
                    {
                        var team = await _teamRepository.GetByIdAsync(instance.TenantId, teamId);
                        if (team != null)
                        {
                            var member = team.Members.FirstOrDefault(m => m.TenantUserId == userId);
                            // The user is authorized if they are in the team and at or above the assigned hierarchy level
                            if (member != null && member.HierarchyOrder >= requiredOrder)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
        }
        
        return false;
    }
}
