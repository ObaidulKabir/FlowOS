using FlowOS.Application.Common.Exceptions;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Core.Interfaces;
using FlowOS.Core.Security;
using FlowOS.Domain.Entities;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using FlowOS.Security.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FlowOS.MCP.Tools;

public sealed class TenantTeamMcpTools
{
    private readonly ITeamRepository _teamRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly ICapabilityService _capabilityService;
    private readonly McpAuthorizationErrorMapper _authorizationErrorMapper;
    private readonly ILogger<TenantTeamMcpTools> _logger;

    public TenantTeamMcpTools(
        ITeamRepository teamRepository,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        ICapabilityService capabilityService,
        McpAuthorizationErrorMapper authorizationErrorMapper,
        ILogger<TenantTeamMcpTools> logger)
    {
        _teamRepository = teamRepository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _capabilityService = capabilityService;
        _authorizationErrorMapper = authorizationErrorMapper;
        _logger = logger;
    }

    public async Task<CallToolResult> ListTenantTeams(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            var denied = await RequireCapabilityAsync(tenantId, "iam.read", "list_tenant_teams");
            if (denied != null) return denied;

            var requiredCapabilities = args["requiredCapabilities"]?.ToObject<List<string>>() ?? new List<string>();

            var teams = await _teamRepository.ListByCapabilitiesAsync(tenantId, requiredCapabilities);

            return McpToolResults.Success(new
            {
                tenantId,
                teams = teams.Select(t => new
                {
                    t.Id,
                    t.Name,
                    t.Description,
                    capabilities = t.Capabilities,
                    hierarchy = t.Hierarchy.Select(h => new { h.Order, h.Name }).OrderBy(h => h.Order).ToList(),
                    memberCount = t.Members.Count
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to list tenant teams: {ex.Message}");
        }
    }

    public async Task<CallToolResult> CreateTenantTeam(JObject args)
    {
        var tenantId = Guid.Empty;
        try
        {
            tenantId = McpTenantResolver.ResolveRequired(args);
            var denied = await RequireMutationAuthorityAsync(args, tenantId, "create_tenant_team");
            if (denied != null) return denied;

            var name = args["name"]?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                return McpToolResults.Fail("MCP-ARG-001", "name is required.");

            var description = args["description"]?.ToString()?.Trim() ?? string.Empty;
            var capabilities = args["capabilities"]?.ToObject<List<string>>() ?? new List<string>();

            var team = new Team(tenantId, name, description, capabilities);

            // Setup default hierarchy if none provided
            var levelsToken = args["hierarchyLevels"] as JArray;
            var hierarchyLevels = new List<TeamHierarchyLevel>();
            if (levelsToken != null && levelsToken.Count > 0)
            {
                foreach (var token in levelsToken)
                {
                    int order = token["order"]?.Value<int>() ?? 0;
                    string levelName = token["name"]?.ToString() ?? $"Level {order}";
                    hierarchyLevels.Add(new TeamHierarchyLevel(levelName, order));
                }
            }
            else
            {
                // Default minimum hierarchy
                hierarchyLevels.Add(new TeamHierarchyLevel("Member", 0));
                hierarchyLevels.Add(new TeamHierarchyLevel("Manager", 1));
            }

            team.SetHierarchy(hierarchyLevels);

            _teamRepository.Add(team);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("Tenant Team {TeamName} ({TeamId}) created through MCP by {ActorId}.", name, team.Id, _currentUser.Id);

            return McpToolResults.Success(new { tenantId, teamId = team.Id, name = team.Name, created = true });
        }
        catch (PolicyViolationException ex)
        {
            return await _authorizationErrorMapper.MapAsync(ex, "create_tenant_team", tenantId);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to create tenant team: {ex.Message}");
        }
    }

    public async Task<CallToolResult> AddTeamMember(JObject args)
    {
        var tenantId = Guid.Empty;
        try
        {
            tenantId = McpTenantResolver.ResolveRequired(args);
            var denied = await RequireMutationAuthorityAsync(args, tenantId, "add_team_member");
            if (denied != null) return denied;

            if (!Guid.TryParse(args["teamId"]?.ToString(), out var teamId))
                return McpToolResults.Fail("MCP-ARG-002", "teamId must be a valid UUID.");

            if (!Guid.TryParse(args["userId"]?.ToString(), out var userId))
                return McpToolResults.Fail("MCP-ARG-002", "userId must be a valid UUID.");

            var levelName = args["levelName"]?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(levelName))
                return McpToolResults.Fail("MCP-ARG-001", "levelName is required.");

            var team = await _teamRepository.GetByIdAsync(tenantId, teamId);
            if (team == null) return McpToolResults.Fail("MCP-NOTFOUND", "Team not found.");

            team.AddMember(userId, levelName);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("Added user {UserId} to team {TeamId} at level {LevelName} through MCP.", userId, teamId, levelName);

            return McpToolResults.Success(new { tenantId, teamId, userId, levelName, added = true });
        }
        catch (PolicyViolationException ex)
        {
            return await _authorizationErrorMapper.MapAsync(ex, "add_team_member", tenantId);
        }
        catch (InvalidOperationException ex)
        {
            return McpToolResults.Fail("MCP-INVALID", ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to add team member: {ex.Message}");
        }
    }

    private async Task<CallToolResult?> RequireMutationAuthorityAsync(JObject args, Guid tenantId, string activity)
    {
        if (args["confirmHumanApproval"]?.Value<bool>() != true)
        {
            return McpToolResults.Fail("MCP-APPROVAL-REQUIRED", $"{activity} changes tenant IAM and requires confirmHumanApproval: true.");
        }

        return await RequireCapabilityAsync(tenantId, "iam.manage", activity);
    }

    private async Task<CallToolResult?> RequireCapabilityAsync(Guid tenantId, string requiredCapability, string activity)
    {
        var capabilities = _currentUser.IsApiKey
            ? await _capabilityService.GetEffectiveCapabilitiesAsync(tenantId, _currentUser.Roles, _currentUser.Scopes, isApiKey: true)
            : await _capabilityService.GetCapabilitiesAsync(tenantId, _currentUser.Roles);

        if (ActivityAuthorization.HasGrant(capabilities, new[] { requiredCapability }))
            return null;

        return McpToolResults.Fail(
            "MCP-AUTHZ-001",
            $"Missing required capability: {requiredCapability}",
            new
            {
                activity,
                tenantId,
                requiredAnyOf = new[] { requiredCapability },
                callerRoles = _currentUser.Roles,
                callerScopes = _currentUser.Scopes,
                effectiveCapabilities = capabilities.OrderBy(item => item).ToArray()
            });
    }
}
