using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Workflows.Domain;
using Microsoft.Extensions.Logging;

namespace FlowOS.Application.Services;

/// <summary>
/// Handles escalating a workflow step's assigned team role to the next hierarchy level.
/// </summary>
public class TeamEscalationService
{
    private readonly ITeamRepository _teamRepository;
    private readonly ILogger<TeamEscalationService> _logger;

    public TeamEscalationService(ITeamRepository teamRepository, ILogger<TeamEscalationService> logger)
    {
        _teamRepository = teamRepository;
        _logger = logger;
    }

    /// <summary>
    /// Attempts to escalate any team-assigned roles on the current step to the next hierarchy level.
    /// Returns true if at least one role was escalated, false if max level reached or no team roles.
    /// </summary>
    public async Task<bool> TryEscalateTeamRolesAsync(
        WorkflowDefinition definition, 
        WorkflowInstance instance, 
        CancellationToken cancellationToken = default)
    {
        if (instance.RoleAssignments.Count == 0) return false;

        var currentStep = definition.Steps.FirstOrDefault(s => s.StepId == instance.CurrentStepId);
        if (currentStep == null || currentStep.AllowedRoles.Count == 0) return false;

        bool escalatedAny = false;

        foreach (var roleName in currentStep.AllowedRoles)
        {
            if (instance.RoleAssignments.TryGetValue(roleName, out var assigneeStr) &&
                assigneeStr.StartsWith("team:", StringComparison.OrdinalIgnoreCase))
            {
                // Format: "team:{teamId}:{hierarchyOrder}"
                var parts = assigneeStr.Split(':');
                if (parts.Length == 3 && 
                    Guid.TryParse(parts[1], out var teamId) && 
                    int.TryParse(parts[2], out var currentOrder))
                {
                    var team = await _teamRepository.GetByIdAsync(instance.TenantId, teamId, cancellationToken);
                    if (team != null)
                    {
                        var nextLevel = team.GetNextHigherLevel(currentOrder);
                        if (nextLevel != null)
                        {
                            // Escalate to next level
                            var newAssigneeStr = $"team:{teamId}:{nextLevel.Order}";
                            instance.AssignRole(roleName, newAssigneeStr);
                            escalatedAny = true;
                            _logger.LogInformation(
                                "Escalated role {RoleName} in instance {InstanceId} to team level {NextLevel} ({NewAssignee})",
                                roleName, instance.Id, nextLevel.Name, newAssigneeStr);
                        }
                    }
                }
            }
        }

        return escalatedAny;
    }
}
